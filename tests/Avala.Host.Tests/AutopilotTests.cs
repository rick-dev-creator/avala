using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class AutopilotTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string FailingChecks = """{ "checks": [ { "name": "tests", "command": "git", "arguments": ["ls-files", "--error-unmatch", "RELEASE-NOTES.md"], "timeoutSeconds": 60 } ] }""";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABacklogRunsUnattendedWithItsCleanJobsMergedAndItsExceptionalJobLeftForReviewAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/permissions.json", Autonomous),
            (".avala/jobs.json", """{ "approval": "merge", "autopilot": { "approve": "cleanEvidence" } }"""),
            (".avala/backlog.json", Backlog(
                ("greet", SimulatedRun.Simulate("edit")),
                ("checks", "[simulate: rewrite-checks] Loosen the checks"),
                ("changelog", "[simulate: follow-up] Start a changelog"))));
        var taken = run.Watch<LoopTaskTaken>();
        var iterated = run.Watch<LoopIterated>();
        var ended = run.Watch<LoopEnded>();
        var before = await run.Repository.GitAsync(Cancellation, "rev-parse", "main");

        var loop = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));

        foreach (var number in (int[])[1, 2, 3])
        {
            _ = await taken.UntilAsync(task => task.Iteration == number);
            _ = await iterated.UntilAsync(iteration => iteration.Iteration.Number == number);
        }

        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.Drained), 3), (state.Ending, state.Iterations));
        var digest = Outcomes.Present(run.Get<IAutopilot>().DigestOf(loop));
        Assert.Equal(
            ["ApprovedAutomatically ", "AwaitingReview RuleFileEdited", "ApprovedAutomatically "],
            digest.Iterations.Select(iteration => $"{iteration.Outcome} {string.Join(',', iteration.Exceptions)}"));
        Assert.All(digest.Iterations, iteration => Assert.NotEmpty(iteration.Cost));
        Assert.Equal(["merge", "merge"], digest.Approvals.Where(approval => approval.Approved).Select(approval => approval.Delivery.Match(delivery => delivery.Strategy, () => string.Empty)));
        var exceptional = Outcomes.Present(digest.Iterations[1].Job);
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.Get<IJobCatalog>().ListAsync(Cancellation), summary => summary.Job == exceptional).Status);
        Assert.Equal(before, await run.Repository.GitAsync(Cancellation, "rev-parse", "main~2"));
        Assert.True(File.Exists(Path.Combine(run.Repository.Path, "GREETING.md")));
        Assert.True(File.Exists(Path.Combine(run.Repository.Path, "CHANGELOG.md")));
        Assert.False(File.Exists(Path.Combine(run.Repository.Path, "calculator.txt")));
        Assert.Empty(await run.Repository.GitAsync(Cancellation, "status", "--porcelain"));
    }

    [Fact]
    public async Task AFailureRepeatedTripsTheBreakerAndLeavesTheRestOfTheBacklogAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", FailingChecks),
            (".avala/backlog.json", Backlog(
                ("one", "[simulate: reply] Greet the team"),
                ("two", "[simulate: reply] Greet the new hires"),
                ("three", "[simulate: reply] Greet the customers"))));
        var taken = run.Watch<LoopTaskTaken>();
        var iterated = run.Watch<LoopIterated>();
        var tripped = run.Watch<BreakerTripped>();
        var ended = run.Watch<LoopEnded>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path) { AttemptsPerRound = 1 }, Cancellation));

        foreach (var number in (int[])[1, 2])
        {
            _ = await taken.UntilAsync(task => task.Iteration == number);
            _ = await iterated.UntilAsync(iteration => iteration.Iteration.Number == number);
        }

        var trip = (await tripped.UntilAsync(_ => true)).Trip;
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal((Breaker.SameFailure, "Check tests Failed exit 1", 2m), (trip.Breaker, trip.Subject, trip.Measured));
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.BreakerTripped), 2), (state.Ending, state.Iterations));
        Assert.Equal(
            [JobStatus.NeedsHelp, JobStatus.NeedsHelp],
            (await run.Get<IJobCatalog>().ListAsync(Cancellation)).Select(summary => summary.Status));
    }

    [Fact]
    public async Task TheMachinesAutopilotFileBoundsALoopStartedWithoutLimitsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("autopilot.json", """{ "iterations": 1 }""")],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/jobs.json", """{ "autopilot": { "approve": "cleanEvidence", "failuresInARow": 2 } }"""),
                (".avala/backlog.json", Backlog(("one", "[simulate: reply] Greet the team"), ("two", "[simulate: reply] Greet the new hires"))),
            ]);
        var tripped = run.Watch<BreakerTripped>();
        var ended = run.Watch<LoopEnded>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        var trip = (await tripped.UntilAsync(_ => true)).Trip;
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal((Breaker.Iterations, 1m, 1m), (trip.Breaker, trip.Measured, trip.Cap));
        Assert.Equal((1, 1, 2), (state.Iterations, state.Limits.Iterations, state.Limits.FailuresInARow));
    }

    [Fact]
    public async Task ALimitNearItsCapPausesTheLoopUntilTheWindowResetsAndThenResumesItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/jobs.json", """{ "autopilot": { "approve": "cleanEvidence" } }"""),
            (".avala/backlog.json", Backlog(("window", "[simulate: near-limit] Use the window"), ("report", "[simulate: reply] Report back"))));
        var paused = run.Watch<LoopPaused>();
        var resumed = run.Watch<LoopResumed>();
        var taken = run.Watch<LoopTaskTaken>();
        var ended = run.Watch<LoopEnded>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(
            new LoopRequest(run.Repository.Path) { Limits = new LoopLimits { PauseAtLimit = 0.9, NothingChanged = 5 } },
            Cancellation));
        var pause = (await paused.UntilAsync(_ => true)).Pause;
        run.AdvanceTo(Outcomes.Present(pause.Until));
        var resumedAt = (await resumed.UntilAsync(_ => true)).At;
        var second = await taken.UntilAsync(task => task.Iteration == 2);
        var state = (await ended.UntilAsync(_ => true)).State;

        var until = Outcomes.Present(pause.Until);
        Assert.Equal((PauseReason.UsageLimit, Option<string>.Some("5h")), (pause.Reason, pause.Window));
        Assert.InRange(until - pause.At, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.True(resumedAt >= until, $"Resumed at {resumedAt:O}, before the reset at {until:O}");
        Assert.Equal("report", second.Task.Key);
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.Drained), 2), (state.Ending, state.Iterations));
    }

    [Fact]
    public async Task TheMachinesDailySpendCeilingPausesTheLoopUntilTheNextDayAndThenItsJobGoesOnToReviewAsync()
    {
        var recording = new DogfoodScript("daily").Turn().Say("Half of it.").Bill(0.0120m, 0.22).Finish().Turn().Say("The rest.").Bill(0.0004m, 0.23).Finish();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("budgets.json", """{ "spendPerDay": { "USD": 0.001 } }"""), ("recordings/daily.json", recording.Json)],
            [
                (".avala/checks.json", PassingChecks),
                (".avala/jobs.json", """{ "autopilot": { "approve": "cleanEvidence" } }"""),
                (".avala/backlog.json", Backlog(("greet", $"{recording.Tag} Greet the team"))),
            ]);
        var paused = run.Watch<LoopPaused>();
        var resumed = run.Watch<LoopResumed>();
        var ended = run.Watch<LoopEnded>();

        var loop = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        var pause = (await paused.UntilAsync(_ => true)).Pause;
        var until = Outcomes.Present(pause.Until);
        run.AdvanceTo(until);
        var resumedAt = (await resumed.UntilAsync(_ => true)).At;
        var state = (await ended.UntilAsync(_ => true)).State;

        var midnight = TimeZoneInfo.ConvertTime(pause.At, run.Clock.LocalTimeZone).Date.AddDays(1);
        Assert.Equal(PauseReason.DailySpend, pause.Reason);
        Assert.Equal(new DateTimeOffset(midnight, run.Clock.LocalTimeZone.GetUtcOffset(midnight)), until);
        Assert.True(resumedAt >= until, $"Resumed at {resumedAt:O}, before the day turned at {until:O}");
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.Drained), 1), (state.Ending, state.Iterations));
        var iteration = Assert.Single(Outcomes.Present(run.Get<IAutopilot>().DigestOf(loop)).Iterations);
        Assert.Equal(IterationOutcome.AwaitingReview, iteration.Outcome);
    }

    [Fact]
    public async Task ALoopPausedForItsHeldJobSettlesTheJobThatSettledBeforeTheLoopStoredItOnceAvalaRestartsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/backlog.json", Backlog(("window", "[simulate: window-runs-out] Use the window"))));
        var taken = run.Watch<LoopTaskTaken>();
        var paused = run.Watch<LoopPaused>();
        var stored = Path.Combine(run.DataFolder, "autopilot.db");
        var whilePaused = Path.Combine(run.DataFolder, "autopilot.db.paused");
        EventWatch<LoopEnded>? ended = null;

        var loop = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        var job = (await taken.UntilAsync(task => task.Iteration == 1)).Job;
        var pause = (await paused.UntilAsync(_ => true)).Pause;
        await run.RestartAfterAsync(TimeSpan.FromHours(2), () => ended = run.Watch<LoopEnded>(), () => File.Copy(stored, whilePaused));
        _ = await ended!.UntilAsync(_ => true);
        await run.RestartAfterAsync(TimeSpan.Zero, () => ended = run.Watch<LoopEnded>(), () => File.Copy(whilePaused, stored, overwrite: true));
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal((PauseReason.UsageLimit, Option<string>.Some("5h")), (pause.Reason, pause.Window));
        Assert.Equal((loop, Option<LoopEnding>.Some(LoopEnding.Drained), 1), (state.Loop, state.Ending, state.Iterations));
        var iteration = Assert.Single(Outcomes.Present(run.Get<IAutopilot>().DigestOf(loop)).Iterations);
        Assert.Equal((Option<JobId>.Some(job), IterationOutcome.AwaitingReview), (iteration.Job, iteration.Outcome));
    }

    [Fact]
    public async Task AnIterationLeftToAPersonFollowsItsJobAfterTheAnswerWhetherTheLoopRunsOnOrHasEndedAndKeepsItAcrossARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/backlog.json", Backlog(("migrate", "[simulate: permission] Migrate the database"), ("rename", "[simulate: steer] Rename the orders module"))));
        var taken = run.Watch<LoopTaskTaken>();
        var iterated = run.Watch<LoopIterated>();
        var followed = run.Watch<LoopIterationFollowed>();
        var autopilot = run.Get<IAutopilot>();

        var loop = Outcomes.Succeeds(await autopilot.StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        var asked = (await iterated.UntilAsync(iteration => iteration.Iteration.Number == 1)).Iteration;
        _ = await taken.UntilAsync(task => task.Iteration == 2);
        var decision = await run.DecisionAsync();
        Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(decision.Session, new PermissionReply(decision.Item, PermissionAnswer.Allow), Cancellation));
        _ = await followed.UntilAsync(iteration => iteration.Iteration.Outcome == IterationOutcome.AwaitingReview);
        var whileRunning = Outcomes.Present(autopilot.DigestOf(loop));
        Outcomes.Succeeds(await autopilot.StopAsync(loop, Cancellation));
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(Outcomes.Present(asked.Job), Cancellation));
        _ = await followed.UntilAsync(iteration => iteration.Iteration.Outcome == IterationOutcome.ApprovedByPerson);
        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Equal(IterationOutcome.AwaitingAnswer, asked.Outcome);
        Assert.Equal((LoopStatus.Running, IterationOutcome.AwaitingReview), (whileRunning.State.Status, whileRunning.Iterations[0].Outcome));
        var ended = Outcomes.Present(autopilot.DigestOf(loop));
        Assert.Equal((LoopStatus.Ended, IterationOutcome.ApprovedByPerson), (ended.State.Status, ended.Iterations[0].Outcome));
        Assert.NotEmpty(ended.Iterations[0].Cost);
    }

    [Fact]
    public async Task AFollowUpProposedByAnAutonomousJobIsAcceptedByTheRulesAndRunsNextAsync()
    {
        await using var run = await FollowUpRunAsync();
        var decided = run.Watch<FollowUpDecided>();
        var taken = run.Watch<LoopTaskTaken>();
        var ended = run.Watch<LoopEnded>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        var decision = (await decided.UntilAsync(_ => true)).Decision;
        var followUp = (await taken.UntilAsync(task => task.Iteration == 2)).Task;
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal(Option<FollowUpRefusal>.None, decision.Refusal);
        Assert.Equal(Option<SourcedTask>.Some(followUp), decision.Task);
        Assert.Equal(("follow-up", "[simulate: reply] Announce the changelog to the team"), (followUp.Source, followUp.Instruction));
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.Drained), 2), (state.Ending, state.Iterations));
    }

    [Fact]
    public async Task AFollowUpProposedByASupervisedJobIsRefusedByThePolicyAsync()
    {
        await using var run = await FollowUpRunAsync();
        var decided = run.Watch<FollowUpDecided>();
        var ended = run.Watch<LoopEnded>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(
            new LoopRequest(run.Repository.Path) { Autonomy = Enum.Parse<Autonomy>("Supervised") },
            Cancellation));
        var decision = (await decided.UntilAsync(_ => true)).Decision;
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal((Option<FollowUpRefusal>.Some(FollowUpRefusal.NotAutonomous), false), (decision.Refusal, decision.Task.IsSome));
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.Drained), 1), (state.Ending, state.Iterations));
    }

    [Fact]
    public async Task ALoopWhoseWorkFailsEndsAsFailedWithItsFaultInsteadOfLookingRunningAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/backlog.json", Backlog(("greet", "[simulate: reply] Greet the team"))));
        var iterated = run.Watch<LoopIterated>();
        var ended = run.Watch<LoopEnded>();
        run.SourceFault.Arm(new IOException("The tracker went away"));

        var loop = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        _ = await iterated.UntilAsync(iteration => iteration.Iteration.Number == 1);
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal(
            (loop, LoopStatus.Ended, Option<LoopEnding>.Some(LoopEnding.Failed), Option<string>.Some("IOException: The tracker went away"), 1),
            (state.Loop, state.Status, state.Ending, state.Fault, state.Iterations));
        Assert.Equal(state, Assert.Single(run.Get<IAutopilot>().Loops()));
        Assert.Equal(AutopilotError.LoopEnded, Outcomes.FailsWith(await run.Get<IAutopilot>().PauseAsync(loop, Cancellation)));
        Assert.Contains("The tracker went away", await run.StopAndReadLogAsync(), StringComparison.Ordinal);
    }

    private Task<SimulatedRun> FollowUpRunAsync() =>
        SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/permissions.json", Autonomous),
            (".avala/jobs.json", """{ "autopilot": { "approve": "cleanEvidence", "followUps": "accept" } }"""),
            (".avala/backlog.json", Backlog(("changelog", "[simulate: follow-up] Start a changelog"))));

    [Fact]
    public async Task ARecurringTaskDueLaterThanASystemTimerCanWaitRunsAgainWhenDueAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/jobs.json", """{ "autopilot": { "approve": "cleanEvidence" } }"""),
            (".avala/backlog.json", $$"""{ "recurring": [ { "id": "report", "instruction": "{{SimulatedRun.Simulate("reply")}}", "everyMinutes": 100000 } ] }"""));
        var waiting = run.Watch<LoopWaiting>();
        var taken = run.Watch<LoopTaskTaken>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        var until = (await waiting.UntilAsync(_ => true)).Until;
        run.AdvanceTo(until);
        var second = await taken.UntilAsync(task => task.Iteration == 2);

        Assert.Equal("report", second.Task.Key);
    }

    private static string Backlog(params (string Id, string Instruction)[] tasks) =>
        $$"""{ "tasks": [ {{string.Join(", ", tasks.Select(task => $$"""{ "id": "{{task.Id}}", "instruction": "{{task.Instruction}}" }"""))}} ] }""";
}
