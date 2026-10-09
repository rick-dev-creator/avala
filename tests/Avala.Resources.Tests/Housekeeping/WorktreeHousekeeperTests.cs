using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Settings;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Tests.Housekeeping;

public sealed class WorktreeHousekeeperTests
{
    private static CancellationToken Cancellation => Resourced.Cancellation;

    [Fact]
    public async Task ADiscardedJobsWorktreeIsReclaimedAtOnceByDefaultAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        var workspace = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);

        var reclaimed = new ReclaimedWorktree(job, home, JobStatus.Discarded, resources.Clock.GetUtcNow());
        Assert.Equal([workspace.Id], resources.Workspaces.Removed);
        Assert.Equal(reclaimed, Assert.Single(resources.Bus.Published.OfType<WorktreeReclaimed>()).Reclaimed);
        Assert.Equal([reclaimed], resources.Housekeeper.Reclaimed());
    }

    [Fact]
    public async Task AFailedJobsWorktreeIsKeptForAWeekThenReclaimedAtTheNextSampleAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Failed), Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);
        Assert.Empty(resources.Workspaces.Removed);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        Assert.Single(resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AnApprovedJobsWorktreeIsKeptByDefaultAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Approved), Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(3650));
        await resources.Housekeeper.SweepAsync(Cancellation);

        Assert.Empty(resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AWorktreeThatCannotBeRemovedIsTriedAgainAtTheNextSweepAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);
        resources.Workspaces.Failure = WorkspaceFailure.GitFailed;

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);
        resources.Workspaces.Failure = null;
        await resources.Housekeeper.SweepAsync(Cancellation);
        await resources.Housekeeper.SweepAsync(Cancellation);

        Assert.Single(resources.Workspaces.Removed);
        Assert.Single(resources.Housekeeper.Reclaimed());
    }

    [Theory]
    [InlineData("Report", false)]
    [InlineData("Clean", true)]
    public async Task AtStartupTheWorktreesAreReconciledAndCleanedOnlyByPolicyAsync(string policy, bool cleans)
    {
        await using var resources = new Resourced(ResourceSettingsParser.Defaults with { Reconcile = Enum.Parse<ReconcilePolicy>(policy) });
        resources.Workspaces.Found = new WorktreeReconciliation([Resourced.Home("stray")], []);

        await resources.Housekeeper.RunAsync(Cancellation);

        var reconciled = Assert.Single(resources.Bus.Published.OfType<WorktreesReconciled>());
        Assert.Equal((resources.Workspaces.Found, cleans), (reconciled.Found, reconciled.Cleaned));
        Assert.Equal(cleans ? [resources.Workspaces.Found] : [], resources.Workspaces.Cleaned);
    }
}
