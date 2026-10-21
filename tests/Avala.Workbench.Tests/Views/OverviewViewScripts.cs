using Avala.Components.Privacy;
using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Components.Graphs;
using Avala.Components.Status;
using Avala.Components.UI.Graphs;
using Avala.Components.UI.Theme;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Overview;
using Avala.Workbench.Timeline;
using Avala.Workbench.Usage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.LogicalTree;
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
            Assert.Equal("2 orchestrations · 5 at work · 1 needs you", view.TextOf("DelegationCaption"));
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
    private static readonly string[] HubTexts = ["ConnectionName", "Provider", "Use"];

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
    public Task HoveringAHubListsEveryLimitWindowOfItsConnectionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel());
            var core = view.Find("Core");

            ToolTip.SetIsOpen(core, true);
            view.Settle();

            var detail = Assert.IsType<StackPanel>(ToolTip.GetTip(core));
            var texts = detail.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
            Assert.Equal(2, detail.GetLogicalDescendants().OfType<ItemsControl>().Single(list => list.Name == "LimitList").ItemCount);
            Assert.Contains("5-hour window", texts);
            Assert.Contains("7-day window", texts);
            Assert.DoesNotContain("This connection reports no usage limit.", texts);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task HoveringAHubShowsItsAccountMaskedAndRevealedOnceTheWindowRevealsPersonalDetailsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel());
            var core = view.Find("Core");
            ToolTip.SetIsOpen(core, true);
            view.Settle();
            var summary = Assert.IsType<StackPanel>(ToolTip.GetTip(core)).Children.OfType<TextBlock>().Single(text => text.Name == "Summary");
            var masked = summary.Text;

            Components.UI.Privacy.PersonalDetails.SetAreHidden(view.Window, false);
            view.Settle();

            Assert.Equal("d•••••@a••••.dev · 9.86 USD · the default connection", masked);
            Assert.Equal("dana@acme.dev · 9.86 USD · the default connection", summary.Text);
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task LongNamesStayInsideTheHubsRingAsync(int agents) =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel(new string('w', 40), "Simulated Claude Code harness", Personal.Plain("local"), false, "no cost", [new DesignLimitViewModel()], [.. Enumerable.Repeat<IAgentViewModel>(new DesignAgentViewModel(), agents)]));
            var hub = view.Find<Panel>("Hub");
            var inside = (hub.Bounds.Width / 2) - 5;
            var corners = HubTexts
                .Select(name => view.Find<TextBlock>(name))
                .SelectMany(text => new[] { new Point(0, 0), new Point(text.Bounds.Width, 0), new Point(0, text.Bounds.Height), new Point(text.Bounds.Width, text.Bounds.Height) }
                    .Select(corner => text.TranslatePoint(corner, hub) ?? throw new InvalidOperationException("not laid out")));

            Assert.All(corners, corner => Assert.True(Math.Sqrt(Math.Pow(corner.X - (hub.Bounds.Width / 2), 2) + Math.Pow(corner.Y - (hub.Bounds.Height / 2), 2)) <= inside, $"{corner} leaves the ring of {hub.Bounds.Width}"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AQuietHubWithoutALimitStaysSmallAndNeutralAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel("pi-local", "Pi", Personal.Plain("local"), false, "no cost", [], [new DesignAgentViewModel()]));

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
            Assert.Equal(MotionLoop.Drift, view.LoopOf("Node"));
            Assert.False(view.HasClass("Node", "attention"));
            Assert.Equal("Running — editing money/minor.go, then re-running the money tests", ToolTip.GetTip(view.Find("Open")));
            Assert.Equal(EdgeKind.Flowing, Graph.GetEdge(view.Named("AgentView").Single()));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnAgentThatNeedsYouIsStillWithAnAmberEdgeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentViewModel(Presenting.SampleJobs.InvoicePdf, "Add invoice PDF endpoint", StatusKind.NeedsYou, "asks a question"));

            Assert.Equal((true, MotionLoop.None), (view.HasClass("Node", "attention"), view.LoopOf("Node")));
            Assert.Equal(EdgeKind.Attention, Graph.GetEdge(view.Named("AgentView").Single()));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task EachOrchestrationIsATreeWithItsOrchestratorOnTopItsFirstLevelSideBySideAndDeeperBranchesBelowTheirParentAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignDelegationViewModel());
            var tree = view.All<AgentTreePanel>().First(panel => panel.IsEffectivelyVisible);
            var nodes = tree.Layout.Nodes;

            Assert.Equal(2, view.Named("OrchestrationView").Count);
            Assert.Equal(AgentTreeShape.Tree, tree.Layout.Shape);
            Assert.Equal([-1, 0, 0, 0, 3, 0], tree.Layout.Parents);
            Assert.True(nodes[4].Y > nodes[3].Y + nodes[3].Height && nodes[1].Y > nodes[0].Y + nodes[0].Height);
            Assert.Equal(nodes[3].Center.X, nodes[4].Center.X, 0.5);
            Assert.Single(view.All<StackPanel>(), panel => panel.Name == "Tree" && panel.IsEffectivelyVisible);
            Assert.False(view.Shows("NoOrchestrators"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task OpenConversationOnAnOpenChildOpensItsConversationAsync() =>
        ui.RunAsync(() =>
        {
            var opened = new List<JobId>();
            var view = Wide(new DesignDelegationViewModel { Trees = [new DesignOrchestrationViewModel { Tree = new DesignSubagentTreeViewModel { Branches = [new DesignOrchestratorCardViewModel(), new DesignDelegationNodeViewModel { IsOpen = true, OpenCommand = new RelayCommand<JobId>(opened.Add) }] } }] });

            view.Click("OpenConversation");

            Assert.Equal([new DesignDelegationNodeViewModel().Job], opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAnOrchestrationTheEmptyStateSaysWhyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignDelegationViewModel { Trees = [] });

            Assert.Equal((true, Presenting.DelegationPhrases.NoneYet, 0), (view.Shows("NoOrchestrators"), view.TextOf("Empty"), view.Named("OrchestrationView").Count));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheFiltersOfferTheStateRepositoryAndConnectionWithTheChosenStateSelectedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignDelegationViewModel());

            Assert.Equal(("Active · 2", "Finished · 5"), (view.Find<Button>("ShowActive").Content, view.Find<Button>("ShowFinished").Content));
            Assert.Equal((true, false), (view.HasClass("ShowActive", "selected"), view.HasClass("ShowFinished", "selected")));
            Assert.Equal((3, 3), (view.Find<ComboBox>("Repository").ItemCount, view.Find<ComboBox>("Connection").ItemCount));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task RefusalsAreListedUnderTheirTreeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignDelegationViewModel { Trees = [new DesignOrchestrationViewModel { Refused = [new DesignDelegationRefusalViewModel()] }] });

            Assert.True(view.Shows("RefusedGroup"));
        }, TestContext.Current.CancellationToken);

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1440;
        view.Window.Height = 900;

        return view.Settle();
    }
}

public sealed class OrchestrationViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnOrchestrationsHeaderShowsItsTotalsAndCollapsesItsTreeAsync() =>
        ui.RunAsync(() =>
        {
            var toggled = 0;
            var view = Screen.Show(new ToggledOrchestration(() => toggled++), ThemeVariant.Dark, 1200, 600);

            view.Click("Toggle");

            Assert.Equal(("Migrate payments to stripe-go v79", "ledger-api", "4 jobs · 3 agents", "1 needs you", "1.1M tokens", "1.47 USD"), (view.TextOf("Title"), view.TextOf("Repository"), view.TextOf("Size"), view.TextOf("NeedsYou"), view.TextOf("Tokens"), view.TextOf("Spent")));
            Assert.Equal(1, toggled);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ACollapsedOrchestrationShowsOnlyItsHeaderAsync() =>
        ui.RunAsync(() =>
        {
            var collapsed = Screen.Show(new DesignOrchestrationViewModel { IsCollapsed = true }, ThemeVariant.Dark, 1200, 600);
            var open = Screen.Show(new DesignOrchestrationViewModel(), ThemeVariant.Dark, 1200, 600);

            Assert.Equal((false, true), (collapsed.Shows("Tree"), open.Shows("Tree")));
        }, TestContext.Current.CancellationToken);

    private sealed class ToggledOrchestration(Action toggle) : IOrchestrationViewModel
    {
        private readonly DesignOrchestrationViewModel design = new();

        public JobId Job => design.Job;

        public string Title => design.Title;

        public string Repository => design.Repository;

        public string Size => design.Size;

        public string NeedsYou => design.NeedsYou;

        public string Tokens => design.Tokens;

        public string Spent => design.Spent;

        public bool IsActive => true;

        public bool IsCollapsed => true;

        public IRelayCommand ToggleCommand { get; } = new RelayCommand(toggle);

        public ISubagentTreeViewModel Tree => design.Tree;

        public IReadOnlyList<IDelegationRefusalViewModel> Refused => design.Refused;
    }
}

public sealed class DelegationNodeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChildIsACardWithWhatItDoesWhatItSpentHowLongItTookAndItsStateAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel(), ThemeVariant.Dark, 420, 220);

            Assert.Equal(
                ("Update call sites in internal/payments", "Edit internal/payments/refund.go", "0.41 USD", "4m 12s", "working", "1"),
                (view.TextOf("Title"), view.TextOf("Doing"), view.TextOf("Spent"), view.TextOf("Took"), view.TextOf("StateText"), view.TextOf("Branches")));
            Assert.False(view.Shows("Details"));
            Assert.Equal((1, EdgeKind.Flowing), (Graph.GetDepth(view.Named("DelegationNodeView").Single()), Graph.GetEdge(view.Named("DelegationNodeView").Single())));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnOpenChildShowsItsHarnessConnectionContextLastActionsAndSpendAgainstItsCarveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel { IsOpen = true }, ThemeVariant.Dark, 420, 600);

            Assert.Equal(("Claude Code", "claude-work", "72% context left", "96.4k tokens"), (view.TextOf("Harness"), view.TextOf("Connection"), view.TextOf("ContextLeft"), view.TextOf("Tokens")));
            Assert.Equal(3, view.Named("RecentActionView").Count);
            Assert.Equal((0.51, true, false), (view.Find<ProgressBar>("Share").Value, view.Shows("Share"), view.Shows("RoleTag")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AChildWhoseHarnessReportedNoContextShowsNoFigureForItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel { Context = ContextGlance.None, IsOpen = true });

            Assert.False(view.Shows("ContextGauge"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingAChildsRowTogglesItAsync() =>
        ui.RunAsync(() =>
        {
            var toggled = 0;
            var view = Screen.Show(new ToggledNode(() => toggled++));

            view.Click("Row");

            Assert.Equal(1, toggled);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AChildCarvedNoCostShowsNoMeterAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel() with { Spend = "0.005 USD spent", HasCarve = false, SpentShare = 0, IsOpen = true });

            Assert.False(view.Shows("Share"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AReadOnlyChildShowsItsRoleWhenOpenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel() with { Role = "reviewer, read-only", IsOpen = true });

            Assert.Equal((true, "reviewer, read-only"), (view.Shows("RoleTag"), view.TextOf("Role")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AChildOnAnotherHarnessThatNeedsYouSaysSoInAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentTreeViewModel().Branches[1]);

            Assert.Equal(("Codex", "waiting"), (view.TextOf("Harness"), view.TextOf("StateText")));
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("StateText").Foreground).Color);
            Assert.True(view.HasClass("Card", "attention"));
        }, TestContext.Current.CancellationToken);

    private sealed record ToggledNode(Action Toggle) : IDelegationNodeViewModel
    {
        private readonly DesignDelegationNodeViewModel design = new();

        public IBranchPlaceViewModel Place => design.Place;

        public DateTimeOffset Since => design.Since;

        public bool IsTiming => design.IsTiming;

        public string Took => design.Took;

        public JobId Job => design.Job;

        public string Title => design.Title;

        public int Depth => design.Depth;

        public JobStatus Status => design.Status;

        public string StateText => design.StateText;

        public string Connection => design.Connection;

        public string Harness => design.Harness;

        public string Activity => design.Activity;

        public string Role => design.Role;

        public string Spend => design.Spend;

        public bool HasCarve => design.HasCarve;

        public double SpentShare => design.SpentShare;

        public string State => design.State;

        public IStatusDotViewModel Dot => design.Dot;

        public bool IsViewing => false;

        public IRelayCommand<JobId> OpenCommand => design.OpenCommand;

        public ContextGlance Context => design.Context;

        public string Tokens => design.Tokens;

        public string Spent => design.Spent;

        public IReadOnlyList<IRecentActionViewModel> Recent => design.Recent;

        public string Doing => design.Doing;

        public bool NeedsYou => false;

        public bool IsSettled => false;

        public bool IsOpen => false;

        public IRelayCommand ToggleCommand { get; } = new RelayCommand(Toggle);

        public IPeekViewModel? Peek => null;

        public bool IsPeeking => false;

        public IRelayCommand PeekCommand => design.PeekCommand;
    }
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

            Assert.Equal(("Migrate payments to stripe-go v79", "Budget 3 USD", "1.47 USD spent"), (view.TextOf("Title"), view.TextOf("Budget"), view.TextOf("Spent")));
            Assert.Equal(4, segments.Count);
            Assert.Equal(segments[0], segments[1], 1);
            Assert.True(segments[3] < segments[2]);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnOrchestratorWithoutABudgetCapShowsNoBudgetLinesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrchestratorCardViewModel { HasBudget = false });

            Assert.Equal((false, false, false), (view.Shows("Budget"), view.Shows("Shares"), view.Shows("ShareNote")));
            Assert.Equal("1.47 USD spent", view.TextOf("Spent"));
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

internal static class NamedViews
{
    public static List<UserControl> Named(this ViewScript view, string type) =>
        [.. view.All<UserControl>().Where(control => control.GetType().Name == type)];
}
