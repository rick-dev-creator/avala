using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.Status;
using Avala.Contests.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Projects.Contracts;
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
    private const string Ledger = "/home/rick/code/ledger";
    private const string Website = "/home/rick/code/website";

    private readonly Bench bench = new();
    private readonly FakeCatalog catalog = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AProjectListsItsOpenJobsNewestFirstAndFoldsItsDoneOnes()
    {
        var ledger = Project("ledger", Ledger);
        var older = Job("Fix the failing test", JobStatus.Running, Ledger);
        var newer = Job("Add an endpoint", JobStatus.Running, Ledger);
        var review = Job("Update the dependency", JobStatus.AwaitingReview, Ledger);
        var merged = Job("Paginate the export", JobStatus.Approved, Ledger);

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.Show(Board(older, newer, review, merged)))
            .Then(sidebar =>
            {
                var group = Group(sidebar, ledger.Name);
                Assert.Equal(["Paginate the export"], group.Done.Select(row => row.Title));
                Assert.Equal(["Update the dependency", "Add an endpoint", "Fix the failing test"], group.Open.Select(row => row.Title));
                Assert.Equal((3, "1 done", false), (group.OpenCount, group.DoneLabel, group.IsDoneShown));
                Assert.Empty(sidebar.NeedsYou);
                Assert.False(sidebar.IsEmpty);
            });
    }

    [Fact]
    public void ProjectsAreListedAlphabeticallyAndAnEmptyOneStaysListed()
    {
        Project("website", Website);
        Project("Side projects");
        Project("ledger", Ledger);

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.Show(Board(Job("Fix the failing test", JobStatus.Running, Ledger))))
            .Then(sidebar => Assert.Equal(["ledger", "Side projects", "website"], sidebar.Projects.Select(group => group.Name)));
    }

    [Fact]
    public void JobsInAScratchRepositoryAreListedUnderChats()
    {
        var scratch = Job("Sketch a parser", JobStatus.AwaitingReview, $"{bench.Scratch.Folder}/2026-10-12-sketch-a-parser-a1b2c3");

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.Show(Board(scratch)))
            .Then(sidebar =>
            {
                Assert.Equal(["Sketch a parser"], sidebar.Chats.Open.Select(row => row.Title));
                Assert.True(sidebar.Chats.HasRows);
                Assert.Empty(sidebar.Projects);
            });
    }

    [Fact]
    public void AJobWhoseProjectHasNotArrivedSitsUnderItsFolderAndMovesIntoTheProjectOnceItArrives()
    {
        var job = Job("Fix the failing test", JobStatus.Running, Ledger);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(job));
        var row = Assert.Single(Group(sidebar, "ledger").Open);

        Project("ledger", Ledger);
        sidebar.Show(Board(job));

        var group = Assert.Single(sidebar.Projects);
        Assert.Equal(("ledger", true), (group.Name, group.IsProject));
        Assert.Same(row, Assert.Single(group.Open));
    }

    [Fact]
    public void NeedsYouListsEveryJobThatNeedsAPersonWhateverItsProjectAndKeepsItsRowAndTheSelection()
    {
        Project("ledger", Ledger);
        Project("website", Website);
        var asking = Job("Run the migration", JobStatus.Running, Ledger);
        var held = Job("Extract sync queue into a module", JobStatus.NeedsHelp, Website) with { Hold = HoldReason.Stalled };
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(asking, held));
        var row = Assert.Single(Group(sidebar, "ledger").Open);
        sidebar.SelectCommand.Execute(row);

        ViewModelScript.Given(sidebar)
            .When(shown => shown.Show(Board(asking with { Transcript = AskingToRun() }, held)))
            .Then(shown =>
            {
                Assert.Empty(Group(shown, "ledger").Open);
                Assert.Equal(["Extract sync queue into a module", "Run the migration"], shown.NeedsYou.Select(entry => entry.Title));
                Assert.Same(row, shown.NeedsYou[1]);
                Assert.Same(row, shown.Selected);
                Assert.True(row.IsSelected);
                Assert.Equal(("wants to run a command", 1, StatusKind.NeedsYou), (row.Fact, row.PendingDecisions, row.Dot.Kind));
                Assert.Equal(("held: stalled", StatusKind.Held), (shown.NeedsYou[0].Fact, shown.NeedsYou[0].Dot.Kind));
            });
    }

    [Fact]
    public void AProjectShowsHowManyJobsAreOpenAndRingsWhenOneOfItsJobsNeedsYou()
    {
        Project("ledger", Ledger);
        Project("website", Website);
        var sidebar = bench.Sidebar();

        sidebar.Show(Board(Job("Run the migration", JobStatus.Running, Ledger) with { Transcript = AskingToRun() }, Job("Add an endpoint", JobStatus.Running, Ledger), Job("Fix the header", JobStatus.Running, Website)));

        Assert.Equal(
            [("ledger", 1, true), ("website", 1, false)],
            sidebar.Projects.Select(group => (group.Name, group.OpenCount, group.NeedsYou)));
    }

    [Fact]
    public void AJobWaitingForItsUsageWindowToResetStaysInItsProjectRatherThanNeedingYou()
    {
        var sidebar = bench.Sidebar();
        var job = Job("Fix the calculator", JobStatus.NeedsHelp);
        var since = DateTimeOffset.UnixEpoch;

        sidebar.Show(Board(job with { Hold = HoldReason.LimitNearlyReached, Wait = new ResetWait(job.Job, new ConnectionName("simulator-one"), "5h", since.AddHours(1), since) }));

        var row = Assert.Single(Group(sidebar, "repo").Open);
        Assert.Empty(sidebar.NeedsYou);
        Assert.Equal(($"resumes at {since.AddHours(1).ToLocalTime():HH:mm}", StatusKind.Held, JobGroup.Waiting), (row.Fact, row.Dot.Kind, row.Group));
    }

    [Fact]
    public async Task CollapsingAProjectOrChatsIsRememberedAndRestoredOnTheNextActivationAsync()
    {
        var repo = Project("repo", "/repo");
        var job = bench.Job("Fix the failing test", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        using var first = bench.Sidebar();
        first.Show(bench.Board.Jobs);

        await Group(first, "repo").ToggleCommand.ExecuteAsync(null);
        await first.Chats.ToggleCommand.ExecuteAsync(null);
        using var second = bench.Sidebar();
        await ViewModelScript.Given(second).WhenPresentedAsync(_ => bench.Post(second.Activate), Cancellation);
        await bench.Ui.ReadAsync(() => true);

        Assert.Equal([SidebarShelves.KeyOf(repo.Project), "chats"], bench.Layout.Remembered.CollapsedGroups.Order(StringComparer.Ordinal));
        Assert.Equal((false, false), await bench.Ui.ReadAsync(() => (Group(second, "repo").IsExpanded, second.Chats.IsExpanded)));
    }

    [Fact]
    public void ASubAgentWaitingForItsParentsAnswerSaysSoWithAStillDot()
    {
        var orchestrator = Job("Migrate the orders database", JobStatus.Running);
        var child = ChildOf(orchestrator, "Migrate the database", JobStatus.Running) with { Transcript = AskingToRun(DecisionDelivery.LeftToParent) };
        var sidebar = bench.Sidebar();

        sidebar.Show(Board(orchestrator, child));

        var row = Assert.Single(Assert.Single(Group(sidebar, "repo").Open).Children);
        Assert.Equal(("waiting for its parent", StatusKind.Held, 0), (row.Fact, row.Dot.Kind, row.PendingDecisions));
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
        var rows = Group(sidebar, "repo").Open;

        ViewModelScript.Given(sidebar)
            .Invoke(nameof(SidebarViewModel.SelectCommand), rows[1])
            .Invoke(nameof(SidebarViewModel.SelectCommand), rows[0])
            .ThenNotified(nameof(SidebarViewModel.Selected))
            .Then(shown =>
            {
                Assert.Equal([first.Job, second.Job], selected);
                Assert.Equal([true, false], rows.Select(row => row.IsSelected));
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
        var row = Group(sidebar, "repo").Open.Single(found => found.Job == steady.Job);

        sidebar.Show(Board(steady, other with { Summary = other.Summary with { Status = JobStatus.AwaitingReview } }));

        Assert.Same(row, Group(sidebar, "repo").Open.Single(found => found.Job == steady.Job));
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

        Assert.Single(await bench.Ui.ReadAsync(() => Rows(sidebar).ToList()));
    }

    [Fact]
    public void ASubAgentsJobSitsUnderItsOrchestratorInsteadOfAGroup()
    {
        var orchestrator = Job("Migrate payments to stripe-go v79", JobStatus.Running);
        var child = ChildOf(orchestrator, "Update call sites in internal/payments", JobStatus.Running);

        ViewModelScript.Given(bench.Sidebar())
            .When(sidebar => sidebar.Show(Board(orchestrator, child)))
            .Then(sidebar =>
            {
                var parent = Assert.Single(Group(sidebar, "repo").Open);
                Assert.Equal(("Migrate payments to stripe-go v79", false), (parent.Title, parent.IsChild));
                Assert.Equal(("Update call sites in internal/payments", true), (Assert.Single(parent.Children).Title, parent.Children[0].IsChild));
            });
    }

    [Fact]
    public void ASubAgentSeenBeforeItsOrchestratorMovesUnderItOnceItArrivesAndStaysThere()
    {
        var orchestrator = Job("Migrate payments to stripe-go v79", JobStatus.Running);
        var child = ChildOf(orchestrator, "Rewrite webhook signature tests", JobStatus.Running);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(child));
        var early = Assert.Single(Group(sidebar, "repo").Open);

        sidebar.Show(Board(orchestrator, child));
        sidebar.Show(Board(orchestrator, child with { Summary = child.Summary with { Status = JobStatus.AwaitingReview } }));

        var parent = Assert.Single(Group(sidebar, "repo").Open);
        Assert.Same(early, Assert.Single(parent.Children));
        Assert.Equal("ready for review", early.Fact);
    }

    [Fact]
    public void SelectingASubAgentSelectsItsJobAlone()
    {
        var orchestrator = Job("Migrate payments to stripe-go v79", JobStatus.Running);
        var child = ChildOf(orchestrator, "Regenerate the API reference", JobStatus.Running);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(orchestrator, child));
        var parent = Assert.Single(Group(sidebar, "repo").Open);

        sidebar.SelectCommand.Execute(parent.Children[0]);

        Assert.Equal((false, true), (parent.IsSelected, parent.Children[0].IsSelected));
    }

    [Fact]
    public void TheCandidatesOfAContestSitUnderOneCollapsedRowThatCountsThoseThatPassed()
    {
        var first = Job("Fix JPY rounding in invoice totals", JobStatus.AwaitingReview) with { Verification = Passed() };
        var second = Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(first, second));

        Contest(ContestStatus.Running, first, second);
        sidebar.Show(Board(first, second));

        var row = Assert.IsType<ContestRowViewModel>(Assert.Single(Group(sidebar, "repo").Open));
        Assert.Equal(("Fix JPY rounding in invoice totals", "2 candidates · 1 passed", false), (row.Title, row.Fact, row.IsExpanded));
        Assert.Empty(row.Children);
        Assert.Equal([(first.Job, "Candidate 1", "verified"), (second.Job, "Candidate 2", "working")], row.Candidates.Select(candidate => (candidate.Job, candidate.Title, candidate.Fact)));
    }

    [Fact]
    public void ADecidedContestWaitsForReviewAndSelectingItFocusesItsWinner()
    {
        var first = Job("Fix JPY rounding in invoice totals", JobStatus.AwaitingReview);
        var winner = Job("Fix JPY rounding in invoice totals", JobStatus.AwaitingReview);
        var sidebar = bench.Sidebar();
        JobSelected? selected = null;
        bench.Messenger.Register<JobSelected>(this, (_, message) => selected = message);
        Contest(ContestStatus.Decided, first, winner);
        sidebar.Show(Board(first, winner));

        var row = Assert.Single(Group(sidebar, "repo").Open);
        sidebar.SelectCommand.Execute(row);

        Assert.Equal((winner.Job, true, JobGroup.ReadyForReview), (row.Job, row.IsSelected, row.Group));
        Assert.Equal(winner.Job, selected?.Job);
    }

    [Theory]
    [InlineData(ContestStatus.Undecided, "NeedsYou")]
    [InlineData(ContestStatus.Closed, "Done")]
    [InlineData(ContestStatus.Judging, "Open")]
    public void AContestRowSitsWhereWhatTheContestNeedsPutsIt(ContestStatus status, string place)
    {
        var first = Job("Fix JPY rounding in invoice totals", JobStatus.NeedsHelp);
        var second = Job("Fix JPY rounding in invoice totals", JobStatus.NeedsHelp);
        var sidebar = bench.Sidebar();
        Contest(status, first, second);

        sidebar.Show(Board(first, second));

        Assert.IsType<ContestRowViewModel>(Assert.Single(place switch
        {
            "NeedsYou" => sidebar.NeedsYou,
            "Done" => Group(sidebar, "repo").Done,
            _ => Group(sidebar, "repo").Open,
        }));
    }

    [Fact]
    public async Task PlusCreatesAProjectAndStartsRenamingItOnceItArrivesAsync()
    {
        var sidebar = bench.Sidebar();
        sidebar.Show(Board());

        await sidebar.CreateProjectCommand.ExecuteAsync(null);
        bench.Board.Publish(bench.ProjectBook.Current);
        sidebar.Show(Board());

        var group = Assert.Single(sidebar.Projects);
        Assert.Equal(["create New project"], bench.ProjectBook.Calls);
        Assert.Equal(("New project", true, "New project"), (group.Name, group.IsRenaming, group.Draft));
    }

    [Fact]
    public void MoveToListsEveryOtherProjectAndNewProjectAndASubAgentOffersNone()
    {
        Project("website", Website);
        Project("ledger", Ledger);
        Project("Side projects");
        var orchestrator = Job("Migrate the orders database", JobStatus.Running, Ledger);
        var child = ChildOf(orchestrator, "Migrate the database", JobStatus.Running);
        var sidebar = bench.Sidebar();

        sidebar.Show(Board(orchestrator, child));

        var parent = Assert.IsType<JobRowViewModel>(Assert.Single(Group(sidebar, "ledger").Open));
        var sub = Assert.IsType<JobRowViewModel>(Assert.Single(parent.Children));
        Assert.Equal(["Side projects", "website", "New project…"], parent.MoveTargets.Select(target => target.Name));
        Assert.Equal((true, false), (parent.CanMove, sub.CanMove));
    }

    [Fact]
    public async Task ChoosingAProjectMovesTheConversationThereAsync()
    {
        Project("ledger", Ledger);
        Project("Side projects");
        var job = Job("Fix the failing test", JobStatus.Running, Ledger);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(job));
        var row = Assert.IsType<JobRowViewModel>(Assert.Single(Group(sidebar, "ledger").Open));

        await row.MoveTargets.Single(target => target.Name == "Side projects").Move.ExecuteAsync(null);
        bench.Board.Publish(bench.ProjectBook.Current);
        sidebar.Show(Board(job));

        Assert.Equal(["move to Side projects"], bench.ProjectBook.Calls);
        Assert.Empty(Group(sidebar, "ledger").Open);
        Assert.Same(row, Assert.Single(Group(sidebar, "Side projects").Open));
        Assert.Equal(["ledger", "New project…"], row.MoveTargets.Select(target => target.Name));
    }

    [Fact]
    public async Task NewProjectMovesTheConversationIntoANewProjectAndStartsRenamingItAsync()
    {
        Project("ledger", Ledger);
        var job = Job("Fix the failing test", JobStatus.Running, Ledger);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(job));
        var row = Assert.IsType<JobRowViewModel>(Assert.Single(Group(sidebar, "ledger").Open));

        await row.MoveTargets.Single(target => target.Name == "New project…").Move.ExecuteAsync(null);
        bench.Board.Publish(bench.ProjectBook.Current);
        sidebar.Show(Board(job));

        Assert.Equal(["create New project", "move to New project"], bench.ProjectBook.Calls);
        var created = Group(sidebar, "New project");
        Assert.Same(row, Assert.Single(created.Open));
        Assert.True(created.IsRenaming);
    }

    [Fact]
    public void ADeletedProjectsConversationsReturnToTheirRepositorysProject()
    {
        Project("ledger", Ledger);
        var side = Project("Side projects");
        var job = Job("Fix the failing test", JobStatus.Running, Ledger);
        bench.ProjectBook.Current = bench.ProjectBook.Current with { Placements = bench.ProjectBook.Current.Placements.Add(job.Job, side.Project) };
        bench.Board.Publish(bench.ProjectBook.Current);
        var sidebar = bench.Sidebar();
        sidebar.Show(Board(job));
        var row = Assert.Single(Group(sidebar, "Side projects").Open);

        bench.Board.Publish(new ProjectMap(bench.ProjectBook.Current.Projects.Remove(side), ImmutableDictionary<JobId, ProjectId>.Empty));
        sidebar.Show(Board(job));

        Assert.Equal(["ledger"], sidebar.Projects.Select(group => group.Name));
        Assert.Same(row, Assert.Single(Group(sidebar, "ledger").Open));
    }

    public void Dispose() => bench.Dispose();

    private ProjectRecord Project(string name, string? repository = null)
    {
        var project = bench.ProjectBook.Add(name, repository);
        bench.Board.Publish(bench.ProjectBook.Current);

        return project;
    }

    private void Contest(ContestStatus status, params BoardJob[] candidates)
    {
        var contest = FakeContests.Contest(status, [.. candidates.Select(candidate => candidate.Job)]) with
        {
            Repository = "/repo",
            Verdict = status == ContestStatus.Decided ? new ContestVerdict([], candidates[^1].Job, DateTimeOffset.UnixEpoch) : Option<ContestVerdict>.None,
        };
        bench.Board.Publish(candidates.ToImmutableDictionary(candidate => candidate.Job, _ => new BoardContest(contest, 1)));
    }

    private static IProjectGroupViewModel Group(SidebarViewModel sidebar, string name) =>
        sidebar.Projects.Single(group => group.Name == name);

    private static Option<Verification.Contracts.VerificationReport> Passed() =>
        new Verification.Contracts.VerificationReport(JobId.New(), 1, Verification.Contracts.VerificationOutcome.Passed, Option<Workspaces.Contracts.FileOrigin>.None, [], GateVerdict.Pass, DateTimeOffset.UnixEpoch);

    private static IEnumerable<ISidebarRow> Rows(SidebarViewModel sidebar) =>
        sidebar.NeedsYou.Concat(sidebar.Projects.Append(sidebar.Chats).SelectMany(group => group.Open.Concat(group.Done)));

    private BoardJob ChildOf(BoardJob parent, string instruction, JobStatus status)
    {
        var job = Job(instruction, status, parent.Summary.Repository);

        return job with { Summary = job.Summary with { Parent = parent.Job } };
    }

    private BoardJob Job(string instruction, JobStatus status, string repository = "/repo")
    {
        var job = new BoardJob(catalog.Add(instruction, status).Summary, Transcript.Empty);

        return job with { Summary = job.Summary with { Repository = repository } };
    }

    private static ImmutableDictionary<JobId, BoardJob> Board(params BoardJob[] jobs) =>
        jobs.ToImmutableDictionary(job => job.Job);

    private static Transcript AskingToRun(DecisionDelivery delivery = DecisionDelivery.LeftToHuman)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var now = DateTimeOffset.UnixEpoch;

        return Transcript.Empty
            .Apply(new TurnStarted(session, turn), now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, delivery, now));
    }
}
