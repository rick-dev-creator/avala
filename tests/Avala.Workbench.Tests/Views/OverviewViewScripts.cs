using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Components.Graphs;
using Avala.Components.Status;
using Avala.Components.UI.Graphs;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Overview;
using Avala.Workbench.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class OverviewViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheOverviewOpensOnTheConnectionsAndSwitchesToTheTreeWithItsCaptionAsync() =>
        ui.RunAsync(() =>
        {
            var overview = new OverviewViewModel(new DesignConnectionsViewModel(), new DesignDelegationViewModel());
            var view = Screen.Show(overview);
            var opened = (view.Shows("Connections"), view.Shows("Delegation"), view.Shows("ConnectionsCaption"));

            view.Click("ShowDelegation");

            Assert.Equal((true, false, true), opened);
            Assert.Equal((false, true, true, false), (view.Shows("Connections"), view.Shows("Delegation"), view.HasClass("ShowDelegation", "selected"), view.HasClass("ShowConnections", "selected")));
            Assert.Equal("Migrate payments to stripe-go v79 · 1 orchestrator, 3 sub-agents", view.TextOf("DelegationCaption"));
        }, TestContext.Current.CancellationToken);
}

public sealed class ConnectionsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task EachConnectionIsAHubWithItsAgentsAroundItAndNoNodeOverlapsAnotherAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignConnectionsViewModel());
            var nodes = Nodes(view);

            Assert.Equal((4, 10), (view.Named("ConnectionCardView").Count, nodes.Count));
            Assert.Equal(10, view.Find<HubGraphPanel>("Graph").Layout.Hubs.Sum(hub => hub.Satellites.Count));
            Assert.DoesNotContain(Pairs(Bounds(view, nodes).Concat(Bounds(view, view.Named("ConnectionCardView")))), pair => pair.First.Intersects(pair.Second));
            Assert.Equal((false, false), (view.Shows("NoConnections"), view.Shows("FileNote")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingAnAgentOpensItsConversationAsync() =>
        ui.RunAsync(() =>
        {
            var fixing = Agent("Fix JPY rounding in invoice totals", JobStatus.Running);
            var connections = new RecordingConnections(Card("claude-work", fixing));
            var view = Wide(connections);

            ClickNode(view, "Fix JPY rounding in invoice totals");

            Assert.Equal([fixing.Job], connections.Opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task DataArrivingWhileTheUserHoversAnAgentKeepsThatAgentUnderThePointerAsync() =>
        ui.RunAsync(() =>
        {
            var fixing = Agent("Fix JPY rounding in invoice totals", JobStatus.Running);
            var adding = Agent("Add invoice PDF endpoint", JobStatus.Running);
            var card = Card("claude-work", fixing, adding);
            var view = Wide(new RecordingConnections(card));
            var hovered = Node(view, "Fix JPY rounding in invoice totals");
            view.Window.MouseMove(Center(view, hovered));
            view.Settle();
            var before = (hovered.GetVisualDescendants().OfType<Button>().Single().IsPointerOver, Center(view, hovered));

            card.Update(State("claude-work", fixing, adding, Agent("Update zod to 3.23", JobStatus.Checking)), Option<double>.None);
            view.Settle();

            Assert.Equal((true, before.Item2), (before.Item1, Center(view, hovered)));
            Assert.Same(hovered, Node(view, "Fix JPY rounding in invoice totals"));
            Assert.True(hovered.GetVisualDescendants().OfType<Button>().Single().IsPointerOver);
            Assert.Equal(3, Nodes(view).Count);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutConnectionsTheEmptyStateExplainsWhereAgentsWillAppearAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new RecordingConnections());

            Assert.Equal((true, 0), (view.Shows("NoConnections"), Nodes(view).Count));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARejectedConnectionsFileIsNotedAboveTheGraphAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new RecordingConnections(Card("claude-work")) { FileNote = "connections.json is rejected: Malformed" });

            Assert.Equal((true, "connections.json is rejected: Malformed"), (view.Shows("FileNote"), view.TextOf("FileNote")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ManyConnectionsAndAgentsStillLayOutWithoutOverlapsAsync() =>
        ui.RunAsync(() =>
        {
            var cards = Enumerable.Range(1, 10)
                .Select(connection => Card($"connection-{connection}", [.. Enumerable.Range(1, 6).Select(agent => Agent($"Agent {agent} of connection {connection}", JobStatus.Running))]))
                .ToArray();
            var view = Wide(new RecordingConnections(cards));
            var nodes = Nodes(view);

            Assert.Equal(60, nodes.Count);
            Assert.DoesNotContain(Pairs(Bounds(view, nodes).Concat(Bounds(view, view.Named("ConnectionCardView")))), pair => pair.First.Intersects(pair.Second));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AGraphLargerThanThePageShrinksSoEveryNodeStaysInSightAsync() =>
        ui.RunAsync(() =>
        {
            var cards = Enumerable.Range(1, 4)
                .Select(connection => Card($"connection-{connection}", [.. Enumerable.Range(1, 6).Select(agent => Agent($"Agent {agent} of connection {connection}", JobStatus.Running))]))
                .ToArray();
            var view = Screen.Show(new RecordingConnections(cards));
            view.Window.Width = 1064;
            view.Window.Height = 840;
            view.Settle();
            var page = new Rect(0, 0, view.Window.Width, view.Window.Height);

            Assert.All(Bounds(view, Nodes(view)), node => Assert.True(page.Contains(node), $"{node} leaves {page}"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task LongNamesAreTrimmedInsideTheirNodeAndHubAsync() =>
        ui.RunAsync(() =>
        {
            var title = string.Concat(Enumerable.Repeat("Refactor the very long module name ", 6));
            var view = Wide(new RecordingConnections(Card(new string('x', 80), Agent(title, JobStatus.Running))));
            var node = Nodes(view).Single();
            var hub = view.Named("ConnectionCardView").Single();

            Assert.Equal(210, node.Bounds.Width);
            Assert.True(hub.Bounds.Width <= 128);
            Assert.All(view.All<TextBlock>().Where(text => text.TextTrimming == TextTrimming.CharacterEllipsis && text.Text is { Length: > 60 }), text => Assert.True(text.Bounds.Width <= 210));
        }, TestContext.Current.CancellationToken);

    internal static ConnectionCardViewModel Card(string name, params BoardJob[] agents) => new(State(name, agents), Option<double>.None);

    internal static ConnectionState State(string name, params BoardJob[] agents) =>
        new(new ConnectionName(name), "Claude Code", false, Option<Agents.Contracts.Sessions.AgentAccount>.None, Pages.Used(1m, new UsageLimit("5h", 0.4, Option<DateTimeOffset>.None)), agents);

    internal static BoardJob Agent(string instruction, JobStatus status) => new(Pages.Summary(instruction, status), Transcript.Empty);

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1440;
        view.Window.Height = 900;

        return view.Settle();
    }

    private static List<UserControl> Nodes(ViewScript view) => view.Named("AgentView");

    private static UserControl Node(ViewScript view, string title) =>
        Nodes(view).Single(node => node.DataContext is IAgentViewModel agent && agent.Title == title);

    private static Point Center(ViewScript view, Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), view.Window) ?? throw new InvalidOperationException("not laid out");

    private static void ClickNode(ViewScript view, string title)
    {
        var center = Center(view, Node(view, title));
        view.Window.MouseDown(center, MouseButton.Left);
        view.Window.MouseUp(center, MouseButton.Left);
        view.Settle();
    }

    private static IEnumerable<Rect> Bounds(ViewScript view, IEnumerable<Visual> visuals) =>
        visuals.Select(visual => new Rect(visual.Bounds.Size).TransformToAABB(visual.TransformToVisual(view.Window) ?? Matrix.Identity));

    private static IEnumerable<(Rect First, Rect Second)> Pairs(IEnumerable<Rect> rects)
    {
        var all = rects.ToList();

        return all.SelectMany((first, index) => all.Skip(index + 1).Select(second => (first, second)));
    }
}

public sealed class ConnectionCardViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AHubNearItsHoldThresholdTurnsItsRingAmberAndSweepsItsUseAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel());

            Assert.Equal(("claude-work", "Claude Code", "5h · 88%"), (view.TextOf("ConnectionName"), view.TextOf("Provider"), view.TextOf("Use")));
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<Arc>("Ring").Stroke).Color);
            Assert.Equal(0.88 * 360, view.Find<Arc>("Ring").SweepAngle, 3);
            Assert.Equal(128, view.Find<Panel>("Hub").Bounds.Width, 1);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AQuietHubWithoutALimitStaysSmallAndNeutralAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel("pi-local", "Pi", "local", false, "no cost", [], [new DesignAgentViewModel()]));

            Assert.Equal(("no limit", 92d, 0d), (view.TextOf("Use"), view.Find<Panel>("Hub").Bounds.Width, view.Find<Arc>("Ring").SweepAngle));
            Assert.Equal(Color.Parse("#A3A3AD"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<Arc>("Ring").Stroke).Color);
        }, TestContext.Current.CancellationToken);
}

public sealed class AgentViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AWorkingAgentFloatsWithAPulsingDotAndItsSummaryWaitsInAToolTipAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentViewModel());

            Assert.Equal("Fix JPY rounding in invoice totals", view.TextOf("Title"));
            Assert.True(view.HasClass("Node", "drift"));
            Assert.False(view.HasClass("Node", "attention"));
            Assert.Equal("Running — editing money/minor.go, then re-running the money tests", ToolTip.GetTip(view.Find("Open")));
            Assert.Equal(EdgeKind.Flowing, Graph.GetEdge(view.Named("AgentView").Single()));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnAgentThatNeedsYouIsStillWithAnAmberEdgeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentViewModel(Presenting.SampleJobs.InvoicePdf, "Add invoice PDF endpoint", StatusKind.NeedsYou, "asks a question"));

            Assert.Equal((true, false), (view.HasClass("Node", "attention"), view.HasClass("Node", "drift")));
            Assert.Equal(EdgeKind.Attention, Graph.GetEdge(view.Named("AgentView").Single()));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheTreeShowsTheOrchestratorCardItsChildrenAndAnEdgeToEachAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignDelegationViewModel());
            var tree = view.All<TreeGraphPanel>().Single();

            Assert.Equal((1, 3), (view.Named("OrchestratorCardView").Count, view.Named("DelegationNodeView").Count));
            Assert.Equal([-1, -1, -1], tree.Layout.Nodes.Select(node => node.Parent));
            Assert.Equal((false, false), (view.Shows("NoOrchestrators"), view.Shows("Orchestrators")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task GrandchildrenHangFromTheirParentInTheNextColumnAsync() =>
        ui.RunAsync(() =>
        {
            var delegation = new RecordingDelegation(
                new DesignDelegationNodeViewModel(),
                new DesignDelegationNodeViewModel(Presenting.SampleJobs.InvoicePdf, "Write the payment step's tests", 2, StatusKind.Working, "Codex", "codex-team", "starting", "no cost", "0.2 USD", 0));
            var view = Wide(delegation);
            var nodes = view.All<TreeGraphPanel>().Single().Layout.Nodes;

            Assert.Equal([-1, 0], nodes.Select(node => node.Parent));
            Assert.True(nodes[1].Bounds.X > nodes[0].Bounds.Right);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingAChildOpensItsConversationAsync() =>
        ui.RunAsync(() =>
        {
            var delegation = new RecordingDelegation(new DesignDelegationNodeViewModel());
            var view = Wide(delegation);
            var card = view.Named("DelegationNodeView").Single();
            var center = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), view.Window)!.Value;

            view.Window.MouseDown(center, MouseButton.Left);
            view.Window.MouseUp(center, MouseButton.Left);
            view.Settle();

            Assert.Equal([new DesignDelegationNodeViewModel().Job], delegation.Opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAnOrchestratorTheEmptyStateShowsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new RecordingDelegation() { Root = null });

            Assert.Equal((true, 0), (view.Shows("NoOrchestrators"), view.Named("DelegationNodeView").Count));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task SeveralOrchestratorsCanBeChosenAboveTheTreeAndRefusalsAreListedAsync() =>
        ui.RunAsync(() =>
        {
            var delegation = new RecordingDelegation(new DesignDelegationNodeViewModel())
            {
                Orchestrators = [new DesignOrchestratorViewModel(), new DesignOrchestratorViewModel(Presenting.SampleJobs.SyncQueue, "Ship the release", JobStatus.Running)],
                Refused = [new DesignDelegationRefusalViewModel()],
            };
            var view = Wide(delegation);

            Assert.Equal((true, 2, true), (view.Shows("Orchestrators"), view.Find<ItemsControl>("Orchestrators").ItemCount, view.Shows("RefusedGroup")));
        }, TestContext.Current.CancellationToken);

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1440;
        view.Window.Height = 900;

        return view.Settle();
    }
}

public sealed class DelegationNodeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChildShowsItsHarnessConnectionStateActivityAndSpendAgainstItsCarveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel());

            Assert.Equal(("Claude Code", "claude-work", "Running", "Edited internal/payments/refund.go · 9 of 14 files", "of 0.8 USD"), (view.TextOf("Harness"), view.TextOf("Connection"), view.TextOf("State"), view.TextOf("Activity"), view.TextOf("Carve")));
            Assert.Equal(0.51, view.Find<ProgressBar>("Share").Value);
            Assert.Equal(1, Graph.GetDepth(view.Named("DelegationNodeView").Single()));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AChildThatNeedsYouSaysSoInAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationViewModel().Children[1]);

            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("State").Foreground).Color);
            Assert.True(view.HasClass("Card", "attention"));
        }, TestContext.Current.CancellationToken);
}

public sealed class OrchestratorCardViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheBudgetIsCarvedAcrossTheTreeInProportionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrchestratorCardViewModel());
            view.Window.Width = 400;
            view.Settle();
            var segments = view.Find<ItemsControl>("Shares").GetVisualDescendants().OfType<ShareBar>().Single().Children.Select(child => child.Bounds.Width).ToList();

            Assert.Equal(("Migrate payments to stripe-go v79", "Budget 3 USD", "1.47 USD spent across the tree"), (view.TextOf("Title"), view.TextOf("Budget"), view.TextOf("Spent")));
            Assert.Equal(4, segments.Count);
            Assert.Equal(segments[0], segments[1], 1);
            Assert.True(segments[3] < segments[2]);
        }, TestContext.Current.CancellationToken);
}

public sealed class OrchestratorViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnOrchestratorShowsItsTitleAndStatusAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrchestratorViewModel());

            Assert.Equal(("Migrate payments to stripe-go v79", "Running"), (view.TextOf("Title"), view.TextOf("Status")));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationRefusalViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARefusalShowsWhatWasAskedAndWhyInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationRefusalViewModel());

            Assert.Equal("Rewrite the payment step in Svelte", view.TextOf("Instruction"));
            Assert.True(view.HasClass("Reason", "failure"));
        }, TestContext.Current.CancellationToken);
}

internal sealed class RecordingConnections(params ConnectionCardViewModel[] cards) : IConnectionsViewModel
{
    private readonly ObservableCollection<IConnectionCardViewModel> connections = [.. cards];

    public List<JobId> Opened { get; } = [];

    public IReadOnlyList<IConnectionCardViewModel> Connections => connections;

    public string FileNote { get; init; } = string.Empty;

    public string Caption => string.Empty;

    public IRelayCommand<JobId> OpenCommand => new RelayCommand<JobId>(Opened.Add);

    public long Revision => 0;

    public event EventHandler<Presented>? Presented
    {
        add { }
        remove { }
    }

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }
}

internal sealed class RecordingDelegation(params IDelegationNodeViewModel[] children) : IDelegationViewModel
{
    public List<JobId> Opened { get; } = [];

    public IReadOnlyList<IOrchestratorViewModel> Orchestrators { get; init; } = [new DesignOrchestratorViewModel()];

    public IReadOnlyList<IDelegationNodeViewModel> Children { get; } = children;

    public IReadOnlyList<IDelegationRefusalViewModel> Refused { get; init; } = [];

    public IOrchestratorViewModel? Selected => Orchestrators.Count > 0 ? Orchestrators[0] : null;

    public IOrchestratorCardViewModel? Root { get; init; } = new DesignOrchestratorCardViewModel();

    public string Caption => string.Empty;

    public IRelayCommand<IOrchestratorViewModel> SelectCommand { get; } = new RelayCommand<IOrchestratorViewModel>(_ => { });

    public IRelayCommand<JobId> OpenCommand => new RelayCommand<JobId>(Opened.Add);

    public long Revision => 0;

    public event EventHandler<Presented>? Presented
    {
        add { }
        remove { }
    }

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }
}

internal static class NamedViews
{
    public static List<UserControl> Named(this ViewScript view, string type) =>
        [.. view.All<UserControl>().Where(control => control.GetType().Name == type)];
}
