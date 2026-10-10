using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Settings;
using Avala.Sdk;

namespace Avala.Resources.Tests.Housekeeping;

public sealed class RetentionRecoveryTests
{
    private static CancellationToken Cancellation => Resourced.Cancellation;

    [Fact]
    public async Task AtStartupAWorktreeWhoseRetentionEndedWhileTheApplicationWasStoppedIsReclaimedAtOnceAsync()
    {
        await using var resources = new Resourced(ResourceSettingsParser.Defaults with { Retention = new WorktreeRetention(TimeSpan.FromHours(1), TimeSpan.FromDays(7), Option<TimeSpan>.None) });
        var home = Resourced.Home("discarded");
        var workspace = resources.Workspaces.Add(home);
        var ended = resources.Clock.GetUtcNow() - TimeSpan.FromHours(2);
        var job = resources.Catalog.Add(JobStatus.Discarded, workspace.Id, ended);

        await resources.Recovery.RunAsync(Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Removed);
        Assert.Equal(new ReclaimedWorktree(job.Job, home, JobStatus.Discarded, resources.Clock.GetUtcNow()), Assert.Single(resources.Bus.Published.OfType<WorktreeReclaimed>()).Reclaimed);
    }

    [Fact]
    public async Task AtStartupAWorktreeStillRetainedIsReclaimedAtTheFirstSampleAfterItsJobsEndPlusItsRetentionAsync()
    {
        await using var resources = new Resourced();
        var workspace = resources.Workspaces.Add(Resourced.Home("failed"));
        _ = resources.Catalog.Add(JobStatus.Failed, workspace.Id, resources.Clock.GetUtcNow() - TimeSpan.FromDays(1));

        await resources.Recovery.RunAsync(Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(6) - TimeSpan.FromSeconds(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);
        Assert.Empty(resources.Workspaces.Removed);

        resources.Clock.Advance(TimeSpan.FromSeconds(1));
        await resources.Tracker.HandleAsync(new ResourcesSampled(await resources.SampleAsync()), Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AtStartupAnApprovedJobsWorktreeAndTheWorktreesOfJobsStillGoingAreKeptAsync()
    {
        await using var resources = new Resourced();
        var approved = resources.Workspaces.Add(Resourced.Home("approved"));
        var running = resources.Workspaces.Add(Resourced.Home("running"));
        _ = resources.Catalog.Add(JobStatus.Approved, approved.Id, DateTimeOffset.UnixEpoch);
        _ = resources.Catalog.Add(JobStatus.Running, running.Id, Option<DateTimeOffset>.None);

        await resources.Recovery.RunAsync(Cancellation);
        resources.Clock.Advance(TimeSpan.FromDays(3650));
        await resources.Housekeeper.SweepAsync(resources.Reaper, Cancellation);

        Assert.Empty(resources.Workspaces.Removed);
    }

    [Fact]
    public async Task AJobThatEndedBeforeItsEndWasRecordedIsRetainedFromItsSubmissionAsync()
    {
        await using var resources = new Resourced();
        var workspace = resources.Workspaces.Add(Resourced.Home("older"));
        _ = resources.Catalog.Add(JobStatus.Failed, workspace.Id, Option<DateTimeOffset>.None);

        await resources.Recovery.RunAsync(Cancellation);

        Assert.Equal([workspace.Id], resources.Workspaces.Removed);
    }
}
