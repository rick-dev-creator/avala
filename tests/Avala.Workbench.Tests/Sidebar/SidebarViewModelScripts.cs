using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class SidebarViewModelScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly FakeCatalog catalog = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void JobsAreGroupedByWhatTheyNeedNewestFirst()
    {
        var older = Job("Fix the failing test", JobStatus.Running);
        var newer = Job("Add an endpoint", JobStatus.Running);
        var review = Job("Update the dependency", JobStatus.AwaitingReview);

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.Show(Board(older, newer, review)))
            .Then(sidebar =>
            {
                Assert.Equal(["Add an endpoint", "Fix the failing test"], sidebar.Running.Select(row => row.Title));
                Assert.Equal(["Update the dependency"], sidebar.ReadyForReview.Select(row => row.Title));
                Assert.Empty(sidebar.NeedsYou);
                Assert.False(sidebar.IsEmpty);
            });
    }

    [Fact]
    public void AJobMovesToTheGroupOfWhatItNeedsAndKeepsItsRowAndTheSelection()
    {
        var running = Job("Run the migration", JobStatus.Running);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(running));
        var row = Assert.Single(sidebar.Running);
        sidebar.SelectCommand.Execute(row);

        ViewModelScript.Given(sidebar)
            .When(shown => shown.Show(Board(running with { Transcript = AskingToRun() })))
            .Then(shown =>
            {
                Assert.Empty(shown.Running);
                Assert.Same(row, Assert.Single(shown.NeedsYou));
                Assert.Same(row, shown.Selected);
                Assert.True(row.IsSelected);
                Assert.Equal(("wants to run a command", 1, StatusKind.NeedsYou), (row.Fact, row.PendingDecisions, row.Dot.Kind));
            });
    }

    [Fact]
    public void ARowTellsWhyItsJobWasHeldWithAStillAmberDot()
    {
        var sidebar = bench.Sidebar();

        sidebar.Show(Board(Job("Extract sync queue into a module", JobStatus.NeedsHelp) with { Hold = HoldReason.Stalled }));

        var row = Assert.Single(sidebar.NeedsYou);
        Assert.Equal(("held: stalled", StatusKind.Held), (row.Fact, row.Dot.Kind));
    }

    [Theory]
    [InlineData("Running", "Working")]
    [InlineData("Checking", "Checking")]
    [InlineData("AwaitingReview", "ReadyForReview")]
    [InlineData("Approved", "Done")]
    [InlineData("Failed", "Failed")]
    public void EachRowsDotTellsTheStateOfItsJob(string status, string kind)
    {
        var sidebar = bench.Sidebar();

        sidebar.Show(Board(Job("Fix JPY rounding in invoice totals", Enum.Parse<JobStatus>(status))));

        Assert.Equal(Enum.Parse<StatusKind>(kind), Rows(sidebar).Single().Dot.Kind);
    }

    [Fact]
    public void SelectingARowAnnouncesTheJobAndMarksOnlyThatRow()
    {
        var first = Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var second = Job("Add invoice PDF endpoint", JobStatus.Running);
        var selected = new List<JobId>();
        bench.Messenger.Register<JobSelected>(this, (_, message) => selected.Add(message.Job));
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(first, second));

        ViewModelScript.Given(sidebar)
            .Invoke(nameof(SidebarViewModel.SelectCommand), sidebar.Running[1])
            .Invoke(nameof(SidebarViewModel.SelectCommand), sidebar.Running[0])
            .ThenNotified(nameof(SidebarViewModel.Selected))
            .Then(shown =>
            {
                Assert.Equal([first.Job, second.Job], selected);
                Assert.Equal([true, false], shown.Running.Select(row => row.IsSelected));
            });
    }

    [Fact]
    public void SelectingNothingChangesNothing()
    {
        var selected = new List<JobId>();
        bench.Messenger.Register<JobSelected>(this, (_, message) => selected.Add(message.Job));

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.SelectCommand.Execute(null))
            .Then(sidebar =>
            {
                Assert.Null(sidebar.Selected);
                Assert.Empty(selected);
            });
    }

    [Fact]
    public void WithoutJobsTheSidebarSaysSoUntilTheFirstArrives()
    {
        var sidebar = bench.Sidebar();
        sidebar.Show(Board());
        var empty = sidebar.IsEmpty;

        sidebar.Show(Board(Job("Fix JPY rounding in invoice totals", JobStatus.Preparing)));

        Assert.Equal((true, false), (empty, sidebar.IsEmpty));
    }

    [Fact]
    public void AnUnchangedJobKeepsItsRowWhenTheBoardMoves()
    {
        var steady = Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var other = Job("Add invoice PDF endpoint", JobStatus.Running);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(steady, other));
        var row = sidebar.Running.Single(found => found.Job == steady.Job);

        sidebar.Show(Board(steady, other with { Summary = other.Summary with { Status = JobStatus.AwaitingReview } }));

        Assert.Same(row, Assert.Single(sidebar.Running));
    }

    [Fact]
    public async Task WhileActiveTheSidebarFollowsTheBoardAndStopsOnceDeactivated()
    {
        using var sidebar = bench.Sidebar();
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));

        await ViewModelScript.Given(sidebar).WhenPresentedAsync(_ => bench.Post(sidebar.Activate), Cancellation);
        await bench.Ui.InvokeAsync(sidebar.Deactivate, Cancellation);
        await sidebar.Following;
        bench.Publish(Bench.OnBoard(bench.Job("Add invoice PDF endpoint", JobStatus.Running)));
        await bench.Ui.ReadAsync(() => true);

        Assert.Single(await bench.Ui.ReadAsync(() => sidebar.Running.ToList()));
    }

    public void Dispose() => bench.Dispose();

    private static IEnumerable<IJobRowViewModel> Rows(SidebarViewModel sidebar) =>
        sidebar.NeedsYou.Concat(sidebar.Running).Concat(sidebar.ReadyForReview).Concat(sidebar.Done);

    private BoardJob Job(string instruction, JobStatus status) =>
        new(catalog.Add(instruction, status).Summary, Transcript.Empty);

    private static ImmutableDictionary<JobId, BoardJob> Board(params BoardJob[] jobs) =>
        jobs.ToImmutableDictionary(job => job.Job);

    private static Transcript AskingToRun()
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var now = DateTimeOffset.UnixEpoch;

        return Transcript.Empty
            .Apply(new TurnStarted(session, turn), now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, now));
    }
}
