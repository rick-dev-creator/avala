using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Looping;
using Avala.Autopilot.Loops;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Avala.Testing;

namespace Avala.Autopilot.Tests.Looping;

public sealed class LoopRestartTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALoopIsRestoredWithItsStateAndDigestEndedLoopsIncludedAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("checks", "Loosen the checks");
        pilot.Backlog.Add("greet", "Greet the team");
        var loop = await pilot.StartAsync();
        await pilot.CleanAsync(1, ".avala/checks.json");
        await pilot.CleanAsync(2);
        await pilot.EndedAsync();
        var digest = StoredJson.Write(Outcomes.Present(pilot.Registry.DigestOf(loop)));

        await using var restarted = await pilot.RestartedAsync();

        Assert.Equal(digest, StoredJson.Write(Outcomes.Present(restarted.Registry.DigestOf(loop))));
        Assert.Equal((loop, LoopStatus.Ended, 2), (Assert.Single(restarted.Registry.Loops()).Loop, restarted.Registry.Loops()[0].Status, restarted.Registry.Loops()[0].Iterations));
    }

    [Fact]
    public async Task ARestoredRunningLoopWithoutAJobTakesItsNextTaskAsync()
    {
        await using var pilot = new Pilot();
        var loop = await StoredAsync(pilot, Begun(pilot));
        pilot.Backlog.Add("greet", "Greet the team");

        await using var restarted = await pilot.RestartedAsync();
        var job = await restarted.TakenAsync(1);

        Assert.Equal(Option<JobId>.Some(job), Assert.Single(restarted.Registry.Loops(), state => state.Loop == loop.Id).Current);
    }

    [Fact]
    public async Task ARestoredLoopSettlesTheJobThatSettledWhileAvalaWasClosedAndTakesItsNextTaskAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("greet", "Greet the team");
        pilot.Backlog.Add("docs", "Document the greeting");
        await pilot.StartAsync();
        var job = await pilot.TakenAsync(1);
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");
        pilot.Jobs.Settled(job, JobStatus.AwaitingReview);

        await using var restarted = await pilot.RestartedAsync();
        var first = await restarted.IteratedAsync(1);
        await restarted.TakenAsync(2);

        Assert.Equal((IterationOutcome.ApprovedAutomatically, Option<JobId>.Some(job)), (first.Outcome, first.Job));
        Assert.Equal([job], pilot.Jobs.Approved);
    }

    [Fact]
    public async Task ARestoredLoopPausedForALimitWhoseResetPassedWhileAvalaWasClosedResumesAtOnceAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("greet", "Greet the team");
        var resets = pilot.Clock.GetUtcNow().AddHours(2);
        pilot.Usage.Limits = [new UsageLimit("5h", 0.95, resets)];
        await pilot.StartAsync();
        _ = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);

        await pilot.Registry.DisposeAsync();
        pilot.Clock.Advance(TimeSpan.FromHours(3));
        await using var restarted = await pilot.RestartedAsync();
        var resumed = await restarted.Bus.WaitForAsync<LoopResumed>(_ => true, Cancellation);
        await restarted.TakenAsync(1);

        Assert.Equal(resets.AddHours(1), resumed.At);
    }

    [Fact]
    public async Task ARestoredLoopPausedForItsHeldJobSettlesTheJobThatSettledWhileAvalaWasClosedAndTakesItsNextTaskAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("greet", "Greet the team");
        pilot.Backlog.Add("docs", "Document the greeting");
        var job = await PausedForItsHeldJobAsync(pilot);
        await pilot.Registry.DisposeAsync();
        pilot.Passed(job);
        pilot.Work.Changed(job, "GREETING.md");
        pilot.Jobs.Settled(job, JobStatus.AwaitingReview);
        pilot.Jobs.ContinueRefusal = JobRejection.NotHeld;
        pilot.Clock.Advance(TimeSpan.FromHours(1));

        await using var restarted = await pilot.RestartedAsync();
        var first = await restarted.IteratedAsync(1);
        await restarted.TakenAsync(2);

        Assert.Equal((IterationOutcome.ApprovedAutomatically, Option<JobId>.Some(job)), (first.Outcome, first.Job));
        Assert.Empty(pilot.Jobs.Continued);
    }

    [Fact]
    public async Task ARestoredLoopPausedForItsJobThatIsStillHeldContinuesItAtTheResetAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("greet", "Greet the team");
        var job = await PausedForItsHeldJobAsync(pilot);
        await pilot.Registry.DisposeAsync();
        pilot.Jobs.Settled(job, JobStatus.NeedsHelp);

        await using var restarted = await pilot.RestartedAsync();
        var paused = Assert.Single(restarted.Registry.Loops()).Status;
        pilot.Clock.Advance(TimeSpan.FromMinutes(30));
        _ = await restarted.Bus.WaitForAsync<LoopResumed>(_ => true, Cancellation);

        Assert.Equal(LoopStatus.Paused, paused);
        Assert.Equal([(job, LoopRunner.ResetMessage)], pilot.Jobs.Continued);
        Assert.Empty(restarted.Bus.Published.OfType<LoopIterated>());
    }

    [Fact]
    public async Task ARestoredLoopAPersonPausedStaysPausedUntilAPersonResumesItAsync()
    {
        await using var pilot = new Pilot();
        var loop = await StoredAsync(pilot, Begun(pilot).Paused(new LoopPause(PauseReason.Command, pilot.Clock.GetUtcNow(), Option<DateTimeOffset>.None)));
        pilot.Backlog.Add("greet", "Greet the team");

        await using var restarted = await pilot.RestartedAsync();
        var paused = Assert.Single(restarted.Registry.Loops()).Status;
        Outcomes.Succeeds(await restarted.Registry.ResumeAsync(loop.Id, Cancellation));
        await restarted.TakenAsync(1);

        Assert.Equal(LoopStatus.Paused, paused);
        Assert.Single(pilot.Jobs.Submitted);
    }

    [Fact]
    public async Task ARestoredLoopKeepsItsRepositoryFromAnotherStartAsync()
    {
        await using var pilot = new Pilot();
        _ = await StoredAsync(pilot, Begun(pilot).Paused(new LoopPause(PauseReason.Command, pilot.Clock.GetUtcNow(), Option<DateTimeOffset>.None)));

        await using var restarted = await pilot.RestartedAsync();

        Assert.Equal(AutopilotError.AlreadyRunning, Outcomes.FailsWith(await restarted.Registry.StartAsync(new LoopRequest(Pilot.Repository), Cancellation)));
    }

    [Fact]
    public async Task ALoopStartedBeforeTheStoreIsRestoredKeepsItsOneRunnerAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("greet", "Greet the team");
        pilot.Backlog.Add("docs", "Document the greeting");
        await pilot.StartAsync();
        await pilot.TakenAsync(1);
        var stored = Assert.Single(await pilot.Loops.LoopsAsync(Cancellation));
        await pilot.Loops.KeepAsync(stored with { Current = Option<Underway>.None }, Cancellation);

        await pilot.Registry.RestoreAsync(Cancellation);
        pilot.Registry.Recover();
        await pilot.CleanAsync(1);
        await pilot.CleanAsync(2);
        await pilot.EndedAsync();

        Assert.Equal([1, 2], pilot.Bus.Published.OfType<LoopIterated>().Select(iterated => iterated.Iteration.Number));
        Assert.Single(pilot.Registry.Loops());
    }

    private static async Task<JobId> PausedForItsHeldJobAsync(Pilot pilot)
    {
        await pilot.StartAsync(new LoopLimits { PauseAtLimit = 0.99 });
        var job = await pilot.TakenAsync(1);
        pilot.Usage.Limits = [new UsageLimit("5h", 0.91, pilot.Clock.GetUtcNow().AddMinutes(30))];
        pilot.Jobs.Attempts(job, Pilot.Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted));
        await pilot.ProgressAsync(job, JobStatus.NeedsHelp);
        await pilot.Feed.HandleAsync(new JobHeld(new JobHold(job, default, HoldReason.LimitNearlyReached, SessionHalt.Interrupted)), Cancellation);
        _ = await pilot.Bus.WaitForAsync<LoopPaused>(_ => true, Cancellation);

        return job;
    }

    private static LoopRecord Begun(Pilot pilot) =>
        LoopRecord.Begin(LoopId.New(), new LoopRequest(Pilot.Repository), new LoopLimits(), pilot.Clock.GetUtcNow());

    private static async Task<LoopRecord> StoredAsync(Pilot pilot, LoopRecord loop)
    {
        await pilot.Loops.KeepAsync(loop, Cancellation);

        return loop;
    }
}
