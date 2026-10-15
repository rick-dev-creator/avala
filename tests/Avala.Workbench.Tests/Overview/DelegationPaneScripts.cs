using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Overview;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Overview;

public sealed class DelegationPaneViewModelScripts : IDisposable
{
    private static readonly SessionId Session = SessionId.New();

    private static readonly TurnId Turn = TurnId.New();

    private readonly TestUiDispatcher ui = new();
    private readonly TreeCatalog catalog = new();
    private readonly JobBoard board = new();
    private readonly StrongReferenceMessenger messenger = new();
    private readonly List<JobId> selected = [];
    private readonly List<DelegationPaneChanged> changes = [];
    private readonly JobSummary root = Pages.Summary("Ship the refunds flow", JobStatus.Running, "claude-work");
    private readonly JobSummary endpoint;
    private readonly JobSummary screen;

    public DelegationPaneViewModelScripts()
    {
        endpoint = Pages.Summary("Refund API endpoint", JobStatus.Running, "claude-work", root.Job);
        screen = Pages.Summary("Refunds screen", JobStatus.NeedsHelp, "personal", root.Job);
        catalog.Jobs.AddRange([root, endpoint, screen]);
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty
            .Add(root.Job, new BoardJob(root, Ran(Agent("plan", "Plan the work", "Split it into three jobs", ItemOutcome.Succeeded))))
            .Add(endpoint.Job, new BoardJob(endpoint, Ran(Agent("explore", "Find the refund call sites", "Reading tests/Payments", null))))
            .Add(screen.Job, new BoardJob(screen, Transcript.Empty)));
        messenger.Register<JobSelected>(this, (_, message) => selected.Add(message.Job));
        messenger.Register<DelegationPaneChanged>(this, (_, message) => changes.Add(message));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenedFromAChildThePaneShowsItsWholeTreeWithTheChildHighlightedAndEachJobsSubagentsAsync()
    {
        using var pane = Pane(endpoint.Job);

        await ui.InvokeAsync(() => messenger.Send(new DelegationPaneRequested(endpoint.Job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");

        var shown = await ui.ReadAsync(() => (
            pane.IsOpen,
            pane.Root?.Title,
            pane.Root?.IsViewing,
            pane.Children.Select(child => (child.Title, child.IsViewing)).ToList(),
            pane.Root?.Subagents.Select(agent => (agent.Name, agent.State)).ToList(),
            pane.Children[0].Subagents.Select(agent => (agent.Name, agent.Activity, agent.State)).ToList(),
            pane.Caption,
            pane.Footer));
        Assert.True(shown.Item1);
        Assert.Equal(("Ship the refunds flow", false), (shown.Item2, shown.Item3));
        Assert.Equal([("Refund API endpoint", true), ("Refunds screen", false)], shown.Item4);
        Assert.Equal([("Plan the work", SubagentState.Done)], shown.Item5);
        Assert.Equal([("Find the refund call sites", "Reading tests/Payments", SubagentState.Running)], shown.Item6);
        Assert.Equal(("3 jobs · 2 agents", "claude-work · 2 jobs   personal · 1 job"), (shown.Item7, shown.Item8));
        Assert.Equal([new DelegationPaneChanged(endpoint.Job, true)], changes);
    }

    [Fact]
    public async Task OnlyTheShownJobsRequestTogglesThePaneAndClickingAJobOpensItAsync()
    {
        using var pane = Pane(endpoint.Job);

        await ui.InvokeAsync(() => messenger.Send(new DelegationPaneRequested(screen.Job)), Cancellation);
        var ignored = await ui.ReadAsync(() => pane.IsOpen);
        await ui.InvokeAsync(() => messenger.Send(new DelegationPaneRequested(endpoint.Job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");
        await ui.InvokeAsync(() => pane.Children[1].OpenCommand.Execute(screen.Job), Cancellation);
        await ui.InvokeAsync(() => messenger.Send(new DelegationPaneRequested(endpoint.Job)), Cancellation);

        Assert.False(ignored);
        Assert.Equal([screen.Job], selected);
        Assert.False(await ui.ReadAsync(() => pane.IsOpen));
        Assert.Equal([new DelegationPaneChanged(endpoint.Job, true), new DelegationPaneChanged(endpoint.Job, false)], changes);
    }

    [Fact]
    public async Task MovingToAJobOutsideAnyTreeClosesThePaneAsync()
    {
        var alone = Pages.Summary("Fix the README", JobStatus.Running);
        board.Publish(board.Jobs.Add(alone.Job, new BoardJob(alone, Transcript.Empty)));
        using var pane = Pane(endpoint.Job);
        await ui.InvokeAsync(() => messenger.Send(new DelegationPaneRequested(endpoint.Job)), Cancellation);

        await ui.InvokeAsync(() => pane.OnRegionContextChanged(alone.Job), Cancellation);

        Assert.False(await ui.ReadAsync(() => pane.IsOpen));
    }

    [Theory]
    [InlineData("root", true)]
    [InlineData("child", true)]
    [InlineData("alone", false)]
    public async Task TheHeaderOffersTheTreeOnlyToAJobThatIsPartOfOneAsync(string which, bool expected)
    {
        var alone = Pages.Summary("Fix the README", JobStatus.Running);
        board.Publish(board.Jobs.Add(alone.Job, new BoardJob(alone, Transcript.Empty)));
        var shown = which switch { "root" => root.Job, "child" => screen.Job, _ => alone.Job };
        using var toggle = new DelegationToggleViewModel(Reader(), board, new LiveFeed(new Pulse(board), ui), messenger);
        toggle.Activate();
        toggle.OnRegionContextChanged(shown);

        await ui.PresentedAsync(toggle, () => toggle.Revision > 0, () => toggle.IsShown.ToString());

        Assert.Equal(expected, await ui.ReadAsync(() => toggle.IsShown));
    }

    [Fact]
    public async Task TheHeaderButtonAsksForThePaneAndShowsItOpenAsync()
    {
        using var toggle = new DelegationToggleViewModel(Reader(), board, new LiveFeed(new Pulse(board), ui), messenger);
        using var pane = Pane(endpoint.Job);
        toggle.OnRegionContextChanged(endpoint.Job);

        await ui.InvokeAsync(() => toggle.ToggleCommand.Execute(null), Cancellation);

        Assert.Equal((true, true), await ui.ReadAsync(() => (pane.IsOpen, toggle.IsOpen)));
    }

    public void Dispose() => ui.Dispose();

    private DelegationReader Reader()
    {
        var usage = new FakeUsage();

        return new DelegationReader(catalog, new FakeDelegations(), Focusing.Spending(usage, new SessionBook(usage)), board);
    }

    private DelegationPaneViewModel Pane(JobId shown)
    {
        var pane = new DelegationPaneViewModel(Reader(), new LiveFeed(new Pulse(board), ui), new JobFocus(new TestRegions(), messenger), messenger);
        pane.Activate();
        pane.OnRegionContextChanged(shown);

        return pane;
    }

    private static IAgentEvent[] Agent(string id, string name, string activity, ItemOutcome? outcome) =>
    [
        new ItemStarted(Session, Turn, new ItemId(id), ItemKind.Subagent, $"Subagent: {name}"),
        new ItemProgressed(Session, Turn, new ItemId(id), activity),
        .. outcome is { } ended ? new IAgentEvent[] { new ItemCompleted(Session, Turn, new ItemId(id), ended) } : [],
    ];

    private static Transcript Ran(IAgentEvent[] events) =>
        events.Prepend(new TurnStarted(Session, Turn)).Aggregate(Transcript.Empty, (current, next) => current.Apply(next, DateTimeOffset.UnixEpoch));
}
