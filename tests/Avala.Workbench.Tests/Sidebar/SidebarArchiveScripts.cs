using System.Collections.Immutable;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class SidebarArchiveScripts : IDisposable
{
    private const string Ledger = "/home/rick/code/ledger";

    private static readonly string[] Titles = ["Fix the failing test", "Update the dependency", "Paginate the export", "Run the migration"];

    private readonly Bench bench = new();
    private readonly FakeCatalog catalog = new();

    public SidebarArchiveScripts()
    {
        bench.ProjectBook.Add("ledger", Ledger);
        bench.Board.Publish(bench.ProjectBook.Current);
    }

    [Fact]
    public void OnlyARowThatNeitherWorksNorNeedsYouOffersArchive()
    {
        var running = Job("Fix the failing test", JobStatus.Running);
        var review = Job("Update the dependency", JobStatus.AwaitingReview);
        var merged = Job("Paginate the export", JobStatus.Approved);
        var held = Job("Run the migration", JobStatus.NeedsHelp);
        var child = ChildOf(review, "Check the lock file", JobStatus.Approved);

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.Show(Board(running, review, merged, held, child)))
            .Then(sidebar =>
            {
                var ledger = Ledgers(sidebar);
                var rows = ledger.Open.Concat(ledger.Done).Concat(sidebar.NeedsYou).ToDictionary(row => row.Title);
                Assert.Equal(
                    [false, true, true, false],
                    Titles.Select(title => rows[title].CanArchive));
                Assert.False(Assert.Single(rows["Update the dependency"].Children).CanArchive);
            });
    }

    [Fact]
    public async Task ArchivingARowTakesItsConversationAndItsSubAgentsOutOfTheSidebarAsync()
    {
        var review = Job("Update the dependency", JobStatus.AwaitingReview);
        var child = ChildOf(review, "Check the lock file", JobStatus.Approved);
        var other = Job("Add an endpoint", JobStatus.Running);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(review, child, other));
        var row = Ledgers(sidebar).Open.Single(found => found.Title == "Update the dependency");

        await row.ArchiveCommand.ExecuteAsync(null);
        Published(sidebar, review, child, other);

        Assert.Equal(["archive 1"], bench.ProjectBook.Calls);
        Assert.Equal(["Add an endpoint"], Ledgers(sidebar).Open.Select(found => found.Title));
        Assert.DoesNotContain(Ledgers(sidebar).Open, found => found.Children.Count > 0);
    }

    [Fact]
    public async Task ArchivingTheSelectedRowSelectsTheNextRowOfItsGroupAsync()
    {
        var newer = Job("Update the dependency", JobStatus.AwaitingReview);
        var older = Job("Add an endpoint", JobStatus.AwaitingReview, Ledger, -1);
        var selected = new List<JobId>();
        bench.Messenger.Register<JobSelected>(this, (_, message) => selected.Add(message.Job));
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(newer, older));
        var rows = Ledgers(sidebar).Open.ToList();
        sidebar.SelectCommand.Execute(rows[0]);

        await rows[0].ArchiveCommand.ExecuteAsync(null);
        Published(sidebar, newer, older);

        Assert.Equal([newer.Job, older.Job], selected);
        Assert.Same(rows[1], sidebar.Selected);
        Assert.True(rows[1].IsSelected);
    }

    [Fact]
    public async Task ARefusedArchiveSaysWhyOnItsRowAsync()
    {
        var review = Job("Update the dependency", JobStatus.AwaitingReview);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(review));
        var row = Assert.Single(Ledgers(sidebar).Open);
        bench.ProjectBook.Refusal = ProjectRejection.StillWorking;

        await row.ArchiveCommand.ExecuteAsync(null);

        Assert.Equal((true, "It is working again; archive it once it stops"), (row.HasRefusal, row.Refusal));
        Assert.Same(row, Assert.Single(Ledgers(sidebar).Open));
    }

    [Fact]
    public async Task ArchiveDoneArchivesEveryEndedConversationOfTheGroupAndKeepsTheOpenOnesAsync()
    {
        var running = Job("Fix the failing test", JobStatus.Running);
        var merged = Job("Paginate the export", JobStatus.Approved);
        var dropped = Job("Try a cache", JobStatus.Discarded);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(running, merged, dropped));
        var ledger = Ledgers(sidebar);
        Assert.True(ledger.ArchiveDoneCommand.CanExecute(null));

        await ledger.ArchiveDoneCommand.ExecuteAsync(null);
        Published(sidebar, running, merged, dropped);

        Assert.Equal(["archive 2"], bench.ProjectBook.Calls);
        Assert.False(ledger.HasDone);
        Assert.Equal(["Fix the failing test"], ledger.Open.Select(row => row.Title));
        Assert.False(ledger.ArchiveDoneCommand.CanExecute(null));
    }

    [Fact]
    public void ARestoredConversationReturnsToItsGroup()
    {
        var merged = Job("Paginate the export", JobStatus.Approved);
        bench.ProjectBook.Current = bench.ProjectBook.Current with { Archived = [merged.Job] };
        bench.Board.Publish(bench.ProjectBook.Current);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(merged));
        var hidden = Ledgers(sidebar).Done.Count;

        bench.ProjectBook.Current = bench.ProjectBook.Current with { Archived = [] };
        Published(sidebar, merged);

        Assert.Equal(0, hidden);
        Assert.Equal(["Paginate the export"], Ledgers(sidebar).Done.Select(row => row.Title));
    }

    [Fact]
    public async Task AContestIsArchivedAsOneRowWithEveryCandidateAsync()
    {
        var first = Job("Fix the rounding", JobStatus.Approved, "/repo");
        var second = Job("Fix the rounding", JobStatus.Discarded, "/repo");
        var contest = FakeContests.Contest(ContestStatus.Closed, first.Job, second.Job) with { Repository = "/repo" };
        bench.Board.Publish(ImmutableDictionary.CreateRange([KeyValuePair.Create(first.Job, new BoardContest(contest, 1)), KeyValuePair.Create(second.Job, new BoardContest(contest, 1))]));
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(first, second));
        var row = Assert.IsType<ContestRowViewModel>(Assert.Single(sidebar.Projects.Single(group => group.Name == "repo").Done));

        await row.ArchiveCommand.ExecuteAsync(null);

        Assert.True(row.CanArchive);
        Assert.Equal(["archive 2"], bench.ProjectBook.Calls);
        Assert.True(bench.ProjectBook.Current.Archived.SetEquals([first.Job, second.Job]));
    }

    public void Dispose() => bench.Dispose();

    private static IProjectGroupViewModel Ledgers(SidebarViewModel sidebar) => sidebar.Projects.Single(group => group.Name == "ledger");

    private void Published(SidebarViewModel sidebar, params BoardJob[] jobs)
    {
        bench.Board.Publish(bench.ProjectBook.Current);
        sidebar.Show(Board(jobs));
    }

    private BoardJob ChildOf(BoardJob parent, string instruction, JobStatus status)
    {
        var job = Job(instruction, status);

        return job with { Summary = job.Summary with { Parent = parent.Job } };
    }

    private BoardJob Job(string instruction, JobStatus status, string repository = Ledger, int hours = 0)
    {
        var job = new BoardJob(catalog.Add(instruction, status).Summary, Transcript.Empty);

        return job with { Summary = job.Summary with { Repository = repository, Submitted = job.Summary.Submitted.AddHours(hours) } };
    }

    private static ImmutableDictionary<JobId, BoardJob> Board(params BoardJob[] jobs) =>
        jobs.ToImmutableDictionary(job => job.Job);
}
