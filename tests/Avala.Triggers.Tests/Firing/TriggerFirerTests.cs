using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Tests.Firing;

public sealed class TriggerFirerTests
{
    private static readonly TriggerId Nightly = new(TriggerId.Machine, "nightly");

    private static readonly string Repository = Repositories.Key(Triggered.Repository);

    [Fact]
    public async Task AFireSubmitsTheTriggersJobAndAuditsItsRunBeforeAnnouncingItAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "connection": "work", "attempts": 5 """);

        var run = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.External, "forge") { Payload = """{ "ref": "main" }""" }, Triggered.Cancellation));

        var request = Assert.Single(triggered.Jobs.Requests);
        Assert.Equal((Repository, "Update the dependencies", 5, "work", Option<Autonomy>.Some(Autonomy.Supervised)), (request.RepositoryPath, request.Instruction, request.AttemptsPerRound, request.Connection.Match(name => name.Value, () => string.Empty), request.Autonomy));
        Assert.Equal((RunOutcome.Submitted, TriggerOrigin.External, "forge", true), (run.Outcome, run.Origin, run.Who, run.Job.IsSome && run.PayloadDigest.IsSome));
        Assert.Equal(run, (await triggered.FiredAsync(_ => true)));
        Assert.Equal([run], triggered.Triggers.RunsOf(Nightly));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Autonomy.Supervised)]
    public async Task ATriggerAskingAutonomousRunsSupervisedInARepositoryThatDoesNotDeclareAutonomousAsync(Autonomy? declared)
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "autonomy": "autonomous" """, world => world.Policies.Declared = declared.ToOption());

        var run = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.Manual, "person"), Triggered.Cancellation));

        Assert.Equal(Option<Autonomy>.Some(Autonomy.Supervised), Assert.Single(triggered.Jobs.Requests).Autonomy);
        Assert.Equal((Autonomy.Autonomous, Autonomy.Supervised, true), (run.Asked, run.Applied, run.AutonomyCapped));
    }

    [Fact]
    public async Task ATriggerAskingAutonomousRunsAutonomousWhereTheRepositoryDeclaresItAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "autonomy": "autonomous" """, world => world.Policies.Declared = Autonomy.Autonomous);

        var run = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.Manual, "person"), Triggered.Cancellation));

        Assert.Equal((Autonomy.Autonomous, false), (run.Applied, run.AutonomyCapped));
    }

    [Fact]
    public async Task AtItsConcurrencyCapATriggerSubmitsNothingAndJobsAwaitingReviewDoNotCountAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "concurrency": 1 """);
        var manual = new FireRequest(TriggerOrigin.Manual, "person");
        var first = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, manual, Triggered.Cancellation));

        var capped = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, manual, Triggered.Cancellation));
        triggered.Jobs.Settle(Outcomes.Present(first.Job), JobStatus.AwaitingReview);
        var again = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, manual, Triggered.Cancellation));

        Assert.Equal((RunOutcome.AtConcurrencyCap, false), (capped.Outcome, capped.Job.IsSome));
        Assert.Equal(RunOutcome.Submitted, again.Outcome);
        Assert.Equal(2, triggered.Jobs.Requests.Count);
    }

    [Fact]
    public async Task ARefusedSubmissionIsARejectedRunWithItsReasonAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """, world => world.Jobs.Refusal = JobRejection.UnknownConnection);

        var run = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.Manual, "person"), Triggered.Cancellation));

        Assert.Equal((RunOutcome.Rejected, Option<JobRejection>.Some(JobRejection.UnknownConnection)), (run.Outcome, run.Rejection));
    }

    [Fact]
    public async Task AnUnknownTriggerOrAPayloadThatIsNotAnObjectIsRefusedWithoutARunAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 } """);

        Assert.Equal(TriggerError.UnknownTrigger, Outcomes.FailsWith(await triggered.Triggers.FireAsync(new TriggerId(TriggerId.Machine, "gone"), new FireRequest(TriggerOrigin.Manual, "person"), Triggered.Cancellation)));
        Assert.Equal(TriggerError.Malformed, Outcomes.FailsWith(await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.External, "forge") { Payload = "[1]" }, Triggered.Cancellation)));
        Assert.Empty(triggered.Triggers.RunsOf(Nightly));
    }

    [Fact]
    public async Task ALoopTargetQueuesATaskStartsALoopAndAttachesTheJobItsLoopTakesAsync()
    {
        await using var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "target": "loop", "concurrency": 2 """);
        var manual = new FireRequest(TriggerOrigin.Manual, "person");

        var run = Outcomes.Succeeds(await triggered.Triggers.FireAsync(Nightly, manual, Triggered.Cancellation));
        _ = await triggered.Triggers.FireAsync(Nightly, manual, Triggered.Cancellation);
        var offered = Outcomes.Succeeds(await triggered.Source.NextAsync(new SourceRequest(LoopId.New(), Triggered.Repository + "/", triggered.Clock.GetUtcNow()), Triggered.Cancellation));
        var task = Outcomes.Present(offered.Task);
        var job = JobId.New();
        await triggered.Source.MarkAsync(task, new TaskMark(TaskState.Taken, job, triggered.Clock.GetUtcNow()), Triggered.Cancellation);

        Assert.Equal(RunOutcome.Enqueued, run.Outcome);
        Assert.Equal((Repository, Option<Autonomy>.Some(Autonomy.Supervised)), (Assert.Single(triggered.World.Autopilot.Started).Repository, triggered.World.Autopilot.Started[0].Autonomy));
        Assert.Equal(("trigger", "Update the dependencies", Repository), (task.Source, task.Instruction, task.Repository));
        Assert.Equal(Option<JobId>.Some(job), triggered.Triggers.RunsOf(Nightly)[0].Job);
        Assert.Single(triggered.Queue.Queued);
    }

    [Fact]
    public async Task ALoopTaskStillQueuedAtStartupIsDroppedAsync()
    {
        var triggered = await StartAsync(""" "schedule": { "everyMinutes": 60 }, "target": "loop" """);
        _ = await triggered.Triggers.FireAsync(Nightly, new FireRequest(TriggerOrigin.Manual, "person"), Triggered.Cancellation);

        await using var restarted = await triggered.RestartAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(RunOutcome.Dropped, Assert.Single(restarted.Triggers.RunsOf(Nightly)).Outcome);
    }

    private static Task<Triggered> StartAsync(string fields, Action<Triggered.Shared>? arrange = null) =>
        Triggered.StartAsync(Declared.Machine(Declared.Trigger(fields)), arrange);
}
