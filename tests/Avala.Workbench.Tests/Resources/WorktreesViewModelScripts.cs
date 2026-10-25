using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Resources;
using Avala.Workbench.Upkeep;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Resources;

public sealed class WorktreesViewModelScripts : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeResources resources = new();
    private readonly FakeHousekeeping housekeeping = new();
    private readonly FakeWorktreeHistory history = new();
    private readonly TestUiDispatcher ui = new();
    private readonly IMessenger messenger = new StrongReferenceMessenger();
    private readonly JobSummary job = Pages.Summary("Add GitHub login", JobStatus.Approved);

    private static DateOnly Today => DateOnly.FromDateTime(Now.ToLocalTime().DateTime);

    [Fact]
    public async Task ThePageShowsTheSpaceTakenTheLimitsTheLargestAndWhatIsFreedNextAsync()
    {
        resources.Latest = new ResourceSample(Now, [], [new FolderUsage("/worktrees/a", 3L << 30) { Job = job.Job, BuildBytes = (3L << 30) / 2 }, new FolderUsage("/worktrees/b", 512L << 20)], 0) { Disk = new DiskSpace(40, 100) };
        housekeeping.Due.Add(new WorktreeDue(job.Job, "/worktrees/a", WorktreeMoment.Undelivered, Now + TimeSpan.FromHours(18)));
        using var page = Page();

        page.Activate();
        await ui.PresentedAsync(page, () => page.Largest.Count == 2, () => $"{page.Largest.Count} largest");

        Assert.Equal(
            ("3.5 GB in 2 worktrees: 1 finished, 1 other", "of a 20 GB budget · 40% of the disk free, keeping 10%"),
            await ui.ReadAsync(() => (page.Space, page.Limits)));
        Assert.Equal(
            [("Add GitHub login", "50% build output", "3 GB"), ("b", "no build output", "512 MB")],
            await ui.ReadAsync(() => page.Largest.Select(row => (row.Title, row.Detail, row.Amount)).ToList()));
        Assert.Equal(("Add GitHub login", "approved, not delivered", "in 18 h"), await ui.ReadAsync(() => (page.Upcoming[0].Title, page.Upcoming[0].Detail, page.Upcoming[0].Amount)));
    }

    [Fact]
    public async Task TheTrendsListEveryDayOfTheSpanAndTheRecoveredSpaceByWhyAsync()
    {
        history.Report = new WorktreeReport(
            [new WorktreeDay(Today, 2L << 30, 2, 1, 2, 1L << 30), new WorktreeDay(Today.AddDays(-2), 1L << 30, 1, 1, 0, 0)],
            [
                new WorktreeEvent(job.Job, "/worktrees/a", JobStatus.Approved, WorktreeMoment.Delivered, 768L << 20, Now) { Lifetime = TimeSpan.FromDays(2) },
                new WorktreeEvent(job.Job, "/worktrees/b", JobStatus.Discarded, WorktreeMoment.Discarded, 256L << 20, Now) { Lifetime = TimeSpan.FromDays(1) },
                new WorktreeEvent(job.Job, "/worktrees/c", JobStatus.Discarded, Option<WorktreeMoment>.None, 0, Now),
            ]);
        using var page = Page();

        page.Activate();
        await ui.PresentedAsync(page, () => page.Days.Count == 7, () => $"{page.Days.Count} days");
        var week = await ui.ReadAsync(() => page.Days.Select(day => (day.Peak, day.Share, day.IsToday)).ToList());
        _ = await ui.ReadAsync(() =>
        {
            page.ShowMonthCommand.Execute(null);

            return true;
        });

        Assert.Equal([("2 GB", 1d, true), ("0 MB", 0d, false), ("1 GB", 0.5, false)], week.Take(3));
        Assert.Equal((30, false, true), await ui.ReadAsync(() => (page.Days.Count, page.ShowsWeek, page.ShowsMonth)));
        Assert.Equal(
            [("on its base branch", "1 worktree", "768 MB"), ("discarded", "1 worktree", "256 MB")],
            await ui.ReadAsync(() => page.Recovered.Select(row => (row.Title, row.Detail, row.Amount)).ToList()));
        Assert.Equal(
            ("A worktree lives 1.5 days on average before it is freed.", "its work deleted"),
            await ui.ReadAsync(() => (page.Lifetime, page.History[2].Detail)));
    }

    [Fact]
    public async Task ALifetimeOfOneUnitIsSaidInTheSingularAsync()
    {
        history.Report = new WorktreeReport(
            [new WorktreeDay(Today, 1L << 30, 1, 1, 1, 1L << 30)],
            [new WorktreeEvent(job.Job, "/worktrees/a", JobStatus.Approved, WorktreeMoment.Delivered, 1L << 30, Now) { Lifetime = TimeSpan.FromSeconds(20) }]);
        using var page = Page();

        page.Activate();
        await ui.PresentedAsync(page, () => page.Lifetime.Length > 0, () => "no lifetime");

        Assert.Equal("A worktree lives 1 minute on average before it is freed.", await ui.ReadAsync(() => page.Lifetime));
    }

    [Fact]
    public async Task AHistoryRowNamesItsJobAndOpensItAsync()
    {
        history.Report = new WorktreeReport([], [new WorktreeEvent(job.Job, "/worktrees/a", JobStatus.Approved, WorktreeMoment.Delivered, 1L << 20, Now)]);
        var selected = new List<JobId>();
        messenger.Register<List<JobId>, JobSelected>(selected, (list, message) => list.Add(message.Job));
        using var page = Page();

        page.Activate();
        await ui.PresentedAsync(page, () => page.History.Count == 1, () => $"{page.History.Count} in history");
        var row = await ui.ReadAsync(() => page.History[0]);
        row.OpenCommand.Execute(null);

        Assert.Equal(("Add GitHub login", true), (row.Title, row.CanOpen));
        Assert.Equal([job.Job], selected);
    }

    [Fact]
    public async Task AShortageSaysThatEveryWorktreeLeftBelongsToAWorkingJobAsync()
    {
        housekeeping.Short = new WorktreeSpace(5L << 30, 1L << 30, Option<DiskSpace>.None, Option<double>.None, Now);
        using var page = Page();

        page.Activate();
        await ui.PresentedAsync(page, () => page.Shortage.Length > 0, () => page.Shortage);

        Assert.Contains("worktrees take 5 GB", await ui.ReadAsync(() => page.Shortage), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeingNowSaysHowMuchWasFreedAsync()
    {
        housekeeping.FreedNow.Add(new FreedWorktree(job.Job, "/worktrees/a", JobStatus.Approved, WorktreeMoment.Requested, 1L << 30, Now));
        using var page = Page();

        await page.FreeNowCommand.ExecuteAsync(null);

        Assert.Equal("Freed 1 worktree, 1 GB.", page.Freed);
    }

    public void Dispose() => ui.Dispose();

    private WorktreesViewModel Page() =>
        new(new WorktreeReader(resources, housekeeping, history, Pages.Board(job)), new LiveFeed(new Pulse(new JobBoard()), ui, NullLogger<LiveFeed>.Instance, new FakeTimeProvider(Now)), new JobFocus(new TestRegions(), messenger));
}
