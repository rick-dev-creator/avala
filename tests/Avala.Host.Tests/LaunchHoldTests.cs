using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class LaunchHoldTests(PublishedPlugins plugins)
{
    private const string Locked = "The process cannot access the file because it is being used by another process.";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobWhoseLaunchThrowsWaitsForAPersonAcrossARestartAndRunsInOneWorktreeOnceRetriedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var holds = run.Watch<JobHeld>();
        run.LaunchFault.Arm(new IOException(Locked));
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var hold = (await holds.UntilAsync(held => held.Hold.Job == job)).Hold;
        run.LaunchFault.Disarm();

        await run.RestartAsync();
        await run.StartedAsync();
        var kept = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary;
        var leftover = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(kept.Workspace), Cancellation)).Path;
        var progress = run.Watch<JobProgressed>();
        var retried = await run.Get<IJobLaunches>().RetryAsync(job, Cancellation);
        var settled = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.AwaitingReview);

        Assert.Equal(
            (HoldReason.CouldNotStart, Option<SessionId>.None, Option<string>.Some(Locked)),
            (hold.Reason, hold.Session, hold.Problem));
        Assert.Equal(
            (JobStatus.NeedsHelp, Option<HoldReason>.Some(HoldReason.CouldNotStart), Option<string>.Some(Locked)),
            (kept.Status, kept.Hold, kept.Problem));
        Assert.Equal((job, JobStatus.AwaitingReview), (Outcomes.Succeeds(retried), settled.Status));
        Assert.False(Directory.Exists(leftover));
        Assert.NotEqual(kept.Workspace, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);
        Assert.Contains(Locked, await run.StopAndReadLogAsync(), StringComparison.Ordinal);
    }
}
