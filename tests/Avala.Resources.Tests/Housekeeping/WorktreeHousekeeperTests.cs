using Avala.Agents.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Housekeeping;
using Avala.Resources.Settings;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Tests.Housekeeping;

public sealed class WorktreeHousekeeperTests
{
    private static CancellationToken Cancellation => Resourced.Cancellation;

    [Fact]
    public async Task ADiscardedJobsWorktreeIsFreedAtOnceByDefaultAsync()
    {
        await using var resources = new Resourced();
        var (job, home, workspace) = await EndedAsync(resources, JobStatus.Discarded);

        var freed = new FreedWorktree(job, home, JobStatus.Discarded, WorktreeMoment.Discarded, 0, resources.Clock.GetUtcNow());
        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
        Assert.Empty(resources.Workspaces.Removed);
        Assert.Equal(freed, Assert.Single(resources.Bus.Published.OfType<WorktreeFreed>()).Freed);
        Assert.Equal([freed], resources.Housekeeper.Freed());
    }

    [Fact]
    public async Task FreeingAWorktreeFirstReapsTheProcessesItsJobLeftRunningAsync()
    {
        await using var resources = new Resourced(ResourceSettingsParser.Defaults with { Orphans = OrphanPolicy.Report });
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        var (session, tree) = await resources.OpenAsync(job, home, members: new TreeProcess(41, "dotnet", 2_048, TimeSpan.FromSeconds(1)));
        await resources.Tracker.HandleAsync(new SessionStopped(session), Cancellation);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);

        Assert.Equal([tree.Id], resources.Trees.Closed);
        Assert.Equal(
            [typeof(OrphansReaped), typeof(WorktreeFreed)],
            resources.Bus.Published.Where(published => published is OrphansReaped or WorktreeFreed).Select(published => published.GetType()));
    }

    [Fact]
    public async Task AFreedWorktreeCarriesTheSizeItsFolderWasLastMeasuredAtAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);
        resources.Folders.Sizes[home] = 4_096;
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Failed), Cancellation);

        Assert.Equal(4_096, Assert.Single(resources.Housekeeper.Freed()).Bytes);
    }

    [Fact]
    public async Task ADiscardedJobsWorkIsDeletedAWeekAfterItEndedAsync()
    {
        await using var resources = new Resourced();
        var (job, home, workspace) = await EndedAsync(resources, JobStatus.Discarded);

        resources.Clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);
        Assert.Empty(resources.Workspaces.Removed);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Removed);
        Assert.Equal(new ReclaimedWorktree(job, home, JobStatus.Discarded, resources.Clock.GetUtcNow()), Assert.Single(resources.Housekeeper.Reclaimed()));
    }

    [Fact]
    public async Task TheWorktreesWaitingToBeFreedAreListedWithWhenAndWhyAsync()
    {
        await using var resources = new Resourced();
        var (job, home, _) = await EndedAsync(resources, JobStatus.Approved);

        Assert.Equal([new WorktreeDue(job, home, WorktreeMoment.Undelivered, resources.Clock.GetUtcNow() + TimeSpan.FromDays(1))], resources.Desk.Upcoming());
    }

    [Fact]
    public async Task AFailedJobsWorkIsDeletedThirtyDaysAfterItEndedAsync()
    {
        await using var resources = new Resourced();
        _ = await EndedAsync(resources, JobStatus.Failed);

        resources.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        Assert.Empty(resources.Workspaces.Removed);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Single(resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AWorkDeletionDueAtOnceRemovesTheWorktreeWithoutFreeingItFirstAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { DeleteDiscarded = TimeSpan.Zero }));
        var (_, _, workspace) = await EndedAsync(resources, JobStatus.Discarded);

        Assert.Equal([workspace.Id], resources.Workspaces.Removed);
        Assert.Empty(resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task AJobThatWorksAgainWhenItsWorktreeComesDueKeepsItAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);
        resources.Jobs.Working.Add(job);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Failed), Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(30));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Empty(resources.Workspaces.Shelved);
        Assert.Empty(resources.Workspaces.Removed);
        Assert.Empty(resources.Housekeeper.Freed());
    }

    [Fact]
    public async Task AnApprovedJobsUndeliveredWorkIsFreedADayAfterApprovalAndNeverDeletedAsync()
    {
        await using var resources = new Resourced();
        var (_, _, workspace) = await EndedAsync(resources, JobStatus.Approved);

        resources.Clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        Assert.Empty(resources.Workspaces.Shelved);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(3650));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
        Assert.Equal(WorktreeMoment.Undelivered, Assert.Single(resources.Housekeeper.Freed()).Moment);
        Assert.Empty(resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AnApprovedJobWhoseWorkIsAlreadyOnItsBaseBranchIsFreedAtOnceAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        var workspace = resources.Workspaces.Add(home);
        resources.Workspaces.Delivered.Add(workspace.Id);
        _ = await resources.OpenAsync(job, home);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Approved), Cancellation);

        Assert.Equal(WorktreeMoment.Delivered, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task AnApprovedJobWithAPullRequestOpenIsFreedAtOnceAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        resources.Facts.AwaitingMerge.Add(job);

        _ = await EndedAsync(resources, JobStatus.Approved, job);

        Assert.Equal(WorktreeMoment.PullRequest, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task WorkMergedOutsideAvalaIsNoticedAtTheNextDeliveryCheckAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None }));
        var (_, _, workspace) = await EndedAsync(resources, JobStatus.Approved);
        resources.Workspaces.Delivered.Add(workspace.Id);

        resources.Clock.Advance(WorktreeHousekeeper.DeliveryCheck - TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        Assert.Empty(resources.Workspaces.Shelved);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Equal(WorktreeMoment.Delivered, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task ArchivingAnEndedJobFreesItsWorktreeAtOnceAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None }));
        var (job, _, workspace) = await EndedAsync(resources, JobStatus.Approved);
        Assert.Empty(resources.Workspaces.Shelved);

        await Moments(resources).HandleAsync(new ConversationsArchived([job]), Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
        Assert.Equal(WorktreeMoment.Archived, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task AJobArchivedBeforeItEndsIsFreedOnceItEndsAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None }));
        var job = JobId.New();
        resources.Facts.ArchivedJobs.Add(job);

        _ = await EndedAsync(resources, JobStatus.Approved, job);

        Assert.Equal(WorktreeMoment.Archived, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task APullRequestMergedFreesItsJobsWorktreeAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None }));
        var (job, _, _) = await EndedAsync(resources, JobStatus.Approved);
        var merged = new PullRequestWatchState(job, new ForgeName("github"), new PullRequestRef(7, new Uri("https://github.com/owner/shop/pull/7"), "avala/job", "main"), OnPullRequest.WatchOnly, 3) { Ended = WatchEnd.Merged };

        await Moments(resources).HandleAsync(new PullRequestWatchChanged(merged), Cancellation);

        Assert.Equal(WorktreeMoment.Delivered, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task APullRequestOfAJobStillWaitingForReviewDoesNotFreeItsWorktreeAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Idle = Option<TimeSpan>.None }));
        var job = JobId.New();
        resources.Jobs.Resting.Add(job);
        _ = await EndedAsync(resources, JobStatus.AwaitingReview, job);
        var open = new PullRequestWatchState(job, new ForgeName("github"), new PullRequestRef(7, new Uri("https://github.com/owner/shop/pull/7"), "avala/job", "main"), OnPullRequest.WatchOnly, 3);

        await Moments(resources).HandleAsync(new PullRequestWatchChanged(open), Cancellation);

        Assert.Empty(resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task AMomentSetToKeepNeverFreesTheWorktreeAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Discarded = Option<TimeSpan>.None, DeleteDiscarded = Option<TimeSpan>.None }));
        _ = await EndedAsync(resources, JobStatus.Discarded);

        resources.Clock.Advance(TimeSpan.FromDays(3650));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Empty(resources.Workspaces.Shelved);
        Assert.Empty(resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AChangedPolicyAppliesToWorktreesAlreadyWaitingAsync()
    {
        await using var resources = new Resourced();
        var (_, _, workspace) = await EndedAsync(resources, JobStatus.Approved);

        _ = await resources.Settings.ChangeWorktreesAsync(resources.Settings.Current.Worktrees with { Undelivered = TimeSpan.Zero }, ReconcilePolicy.Clean, Cancellation);
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task AWorktreeThatCannotBeFreedIsTriedAgainTenMinutesLaterAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(job, home);
        resources.Workspaces.Failure = WorkspaceFailure.GitFailed;

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);
        resources.Workspaces.Failure = null;
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        Assert.Empty(resources.Workspaces.Shelved);
        resources.Clock.Advance(WorktreeHousekeeper.DeliveryCheck);
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Single(resources.Workspaces.Shelved);
        Assert.Single(resources.Housekeeper.Freed());
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

    [Fact]
    public async Task AJobWaitingForReviewUntouchedForThreeDaysIsFreedAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        resources.Jobs.Resting.Add(job);
        var (_, _, workspace) = await EndedAsync(resources, JobStatus.AwaitingReview, job);

        resources.Clock.Advance(TimeSpan.FromDays(3) - TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        Assert.Empty(resources.Workspaces.Shelved);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
        Assert.Equal((WorktreeMoment.Idle, JobStatus.AwaitingReview), (Assert.Single(resources.Housekeeper.Freed()).Moment, Assert.Single(resources.Housekeeper.Freed()).Status));
    }

    [Fact]
    public async Task AWaitingJobWhoseAgentIsStillOpenKeepsItsWorktreeAsync()
    {
        await using var resources = new Resourced();
        _ = await EndedAsync(resources, JobStatus.NeedsHelp);

        resources.Clock.Advance(TimeSpan.FromDays(30));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Empty(resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task AWaitingJobWhoseAgentEndsAfterItsIdleTimeIsFreedAtTheNextTryAsync()
    {
        await using var resources = new Resourced();
        var (job, _, workspace) = await EndedAsync(resources, JobStatus.AwaitingReview);
        resources.Clock.Advance(TimeSpan.FromDays(3));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        resources.Jobs.Resting.Add(job);
        resources.Clock.Advance(WorktreeHousekeeper.DeliveryCheck);
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task AFreedWaitingWorktreeRebuiltWithoutItsJobMovingIsFreedAgainOnceIdleAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        resources.Jobs.Resting.Add(job);
        var (_, _, workspace) = await EndedAsync(resources, JobStatus.AwaitingReview, job);
        resources.Clock.Advance(TimeSpan.FromDays(3));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        _ = await resources.Workspaces.RebuildAsync(workspace.Id, Cancellation);
        resources.Clock.Advance(WorktreeHousekeeper.DeliveryCheck);
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(3));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Equal([workspace.Id, workspace.Id], resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task ArchivingAJobWaitingForReviewFreesItsWorktreeAtOnceAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        resources.Jobs.Resting.Add(job);
        _ = await EndedAsync(resources, JobStatus.AwaitingReview, job);

        await Moments(resources).HandleAsync(new ConversationsArchived([job]), Cancellation);

        Assert.Equal(WorktreeMoment.Archived, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task AJobThatWorksAgainStartsItsIdleTimeOverAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        resources.Jobs.Resting.Add(job);
        _ = await EndedAsync(resources, JobStatus.AwaitingReview, job);

        resources.Clock.Advance(TimeSpan.FromDays(2));
        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);
        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.AwaitingReview), Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(2));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Empty(resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task WorktreesOverTheSpaceBudgetAreFreedTheLongestIdleFirstAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None, SpaceBudget = 10L }));
        var (older, olderHome, olderWorkspace) = await EndedAsync(resources, JobStatus.Approved, home: "older");
        resources.Clock.Advance(TimeSpan.FromHours(1));
        var (_, newerHome, _) = await EndedAsync(resources, JobStatus.Approved, home: "newer");
        resources.Folders.Sizes[olderHome] = 8;
        resources.Folders.Sizes[newerHome] = 8;

        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        var freed = Assert.Single(resources.Housekeeper.Freed());
        Assert.Equal((older, WorktreeMoment.Space, 8L), (freed.Job, freed.Moment, freed.Bytes));
        Assert.Equal([olderWorkspace.Id], resources.Workspaces.Shelved);
        Assert.True(resources.Housekeeper.Shortage().IsNone);
    }

    [Fact]
    public async Task AFolderFreedEarlierCountsAgainOnceANewerMeasurementSeesItAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None, SpaceBudget = 10L }));
        var (_, olderHome, olderWorkspace) = await EndedAsync(resources, JobStatus.Approved, home: "older");
        resources.Clock.Advance(TimeSpan.FromHours(1));
        var (newer, newerHome, _) = await EndedAsync(resources, JobStatus.Approved, home: "newer");
        resources.Folders.Sizes[olderHome] = 8;
        resources.Folders.Sizes[newerHome] = 8;
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        _ = await resources.Workspaces.RebuildAsync(olderWorkspace.Id, Cancellation);
        resources.Clock.Advance(TimeSpan.FromMinutes(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        Assert.Equal(newer, resources.Housekeeper.Freed()[^1].Job);
    }

    [Fact]
    public async Task ADiskBelowItsFloorFreesWorktreesUntilEnoughIsFreeAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None }));
        var (_, home, workspace) = await EndedAsync(resources, JobStatus.Approved);
        resources.Folders.Sizes[home] = 10;
        resources.Folders.Space = new DiskSpace(5, 100);

        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
        Assert.Equal(WorktreeMoment.Space, Assert.Single(resources.Housekeeper.Freed()).Moment);
    }

    [Fact]
    public async Task WhenNothingLeftCanBeFreedTheShortageIsAnnouncedOnceAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { SpaceBudget = 1L }));
        var home = Resourced.Home("running");
        _ = resources.Workspaces.Add(home);
        _ = await resources.OpenAsync(JobId.New(), home);
        resources.Folders.Sizes[home] = 10;

        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        var shortage = Assert.Single(resources.Bus.Published.OfType<WorktreeSpaceShort>()).Space;
        Assert.Equal((10L, Option<long>.Some(1L), false), (shortage.Worktrees, shortage.Budget, shortage.Within));
        Assert.Equal(Option<WorktreeSpace>.Some(shortage), resources.Desk.Shortage());
        Assert.Empty(resources.Workspaces.Shelved);
    }

    [Fact]
    public async Task FreeingNowFreesEveryWaitingWorktreeWhateverItsDelayAsync()
    {
        await using var resources = new Resourced(WithPolicy(policy => policy with { Undelivered = Option<TimeSpan>.None }));
        var (job, _, workspace) = await EndedAsync(resources, JobStatus.Approved);

        var freed = await resources.Desk.FreeNowAsync(Cancellation);

        Assert.Equal([(job, WorktreeMoment.Requested)], freed.Select(release => (release.Job, release.Moment)));
        Assert.Equal([workspace.Id], resources.Workspaces.Shelved);
    }

    private static ResourceSettings WithPolicy(Func<WorktreePolicy, WorktreePolicy> change) =>
        ResourceSettingsParser.Defaults with { Worktrees = change(ResourceSettingsParser.Defaults.Worktrees) };

    private static WorktreeMoments Moments(Resourced resources) => new(resources.Housekeeper, resources.Reaper);

    private static async Task<(JobId Job, string Home, WorkspaceInfo Workspace)> EndedAsync(Resourced resources, JobStatus status, JobId? job = null, string home = "one")
    {
        var ended = job ?? JobId.New();
        var folder = Resourced.Home(home);
        var workspace = resources.Workspaces.Add(folder);
        _ = await resources.OpenAsync(ended, folder);
        await resources.Tracker.HandleAsync(new JobProgressed(ended, status), Cancellation);

        return (ended, folder, workspace);
    }
}
