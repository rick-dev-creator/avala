using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class SidebarViewModelTests
{
    private readonly FakeCatalog catalog = new();

    [Fact]
    public void JobsAreGroupedByWhatTheyNeedNewestFirst()
    {
        var older = Job("Fix the failing test", JobStatus.Running);
        var newer = Job("Add an endpoint", JobStatus.Running);
        var review = Job("Update the dependency", JobStatus.AwaitingReview);
        var sidebar = new SidebarViewModel(new Bench().Decisions());

        sidebar.Show(Board(older, newer, review));

        Assert.Equal(["Add an endpoint", "Fix the failing test"], sidebar.Running.Select(row => row.Title));
        Assert.Equal(["Update the dependency"], sidebar.ReadyForReview.Select(row => row.Title));
        Assert.Empty(sidebar.NeedsYou);
    }

    [Fact]
    public void AJobMovesToTheGroupOfWhatItNeedsAndKeepsItsRowAndTheSelection()
    {
        var running = Job("Run the migration", JobStatus.Running);
        var sidebar = new SidebarViewModel(new Bench().Decisions());
        sidebar.Show(Board(running));
        var row = Assert.Single(sidebar.Running);
        sidebar.SelectCommand.Execute(row);

        sidebar.Show(Board(running with { Transcript = AskingToRun() }));

        Assert.Empty(sidebar.Running);
        Assert.Same(row, Assert.Single(sidebar.NeedsYou));
        Assert.Same(row, sidebar.Selected);
        Assert.Equal(("wants to run a command", 1, 1), (row.Fact, row.PendingDecisions, sidebar.PendingDecisions));
    }

    [Fact]
    public void ARowTellsWhyItsJobWasHeld()
    {
        var sidebar = new SidebarViewModel(new Bench().Decisions());

        sidebar.Show(Board(Job("Fix the failing test", JobStatus.NeedsHelp) with { Hold = HoldReason.Stalled }));

        Assert.Equal("held: stalled", Assert.Single(sidebar.NeedsYou).Fact);
    }

    [Fact]
    public void ATitleIsTheFirstLineOfTheInstructionCutShort()
    {
        var title = FactPhrases.Title($"{new string('a', 100)}\nMore detail");

        Assert.Equal((80, '…'), (title.Length, title[^1]));
    }

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
