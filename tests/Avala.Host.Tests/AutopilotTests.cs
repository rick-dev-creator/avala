using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
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
        var ended = run.Watch<LoopEnded>();
        var before = await run.Repository.GitAsync(Cancellation, "rev-parse", "main");

        var loop = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
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
        var tripped = run.Watch<BreakerTripped>();
        var ended = run.Watch<LoopEnded>();

        _ = Outcomes.Succeeds(await run.Get<IAutopilot>().StartAsync(new LoopRequest(run.Repository.Path) { AttemptsPerRound = 1 }, Cancellation));
        var trip = (await tripped.UntilAsync(_ => true)).Trip;
        var state = (await ended.UntilAsync(_ => true)).State;

        Assert.Equal((Breaker.SameFailure, "Check tests Failed exit 1", 2m), (trip.Breaker, trip.Subject, trip.Measured));
        Assert.Equal((Option<LoopEnding>.Some(LoopEnding.BreakerTripped), 2), (state.Ending, state.Iterations));
        Assert.Equal(
            [JobStatus.NeedsHelp, JobStatus.NeedsHelp],
            (await run.Get<IJobCatalog>().ListAsync(Cancellation)).Select(summary => summary.Status));
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

    private Task<SimulatedRun> FollowUpRunAsync() =>
        SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/permissions.json", Autonomous),
            (".avala/jobs.json", """{ "autopilot": { "approve": "cleanEvidence", "followUps": "accept" } }"""),
            (".avala/backlog.json", Backlog(("changelog", "[simulate: follow-up] Start a changelog"))));

    private static string Backlog(params (string Id, string Instruction)[] tasks) =>
        $$"""{ "tasks": [ {{string.Join(", ", tasks.Select(task => $$"""{ "id": "{{task.Id}}", "instruction": "{{task.Instruction}}" }"""))}} ] }""";
}
