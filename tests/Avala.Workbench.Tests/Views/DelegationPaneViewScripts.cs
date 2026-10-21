using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia;
using Avalonia.Controls;
using Avala.Components.Graphs;
using Avala.Components.UI.Graphs;
using Avala.Components.UI.Theme;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class DelegationPaneViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePaneHangsEveryCardOnARailUnderTheOrchestratorWithTheViewedOneMarkedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationPaneViewModel(), ThemeVariant.Dark, 420, 1100);
            var panel = view.All<AgentTreePanel>().Single();
            var cards = view.All<Border>().Where(card => card.Name == "Card").ToList();

            Assert.Equal(("6 jobs · 3 agents", "claude-work · 3 jobs   claude · 2 jobs"), (view.TextOf("Caption"), view.TextOf("Footer")));
            Assert.Equal(AgentTreeShape.Rail, panel.Layout.Shape);
            Assert.Equal(cards.OrderBy(card => card.TranslatePoint(default, view.Window)?.Y).ToList(), cards);
            Assert.Single(cards, card => card.Classes.Contains("viewing"));
        }, Cancellation);

    [Fact]
    public Task ThePaneClosesFromItsHeaderAsync() =>
        ui.RunAsync(() =>
        {
            var closed = 0;
            var pane = new ClosablePane(() => closed++);
            var view = Screen.Show(pane, ThemeVariant.Dark, 420, 600);

            view.Click("Close");

            Assert.Equal(1, closed);
        }, Cancellation);

    private sealed class ClosablePane(Action close) : IDelegationPaneViewModel
    {
        public ISubagentTreeViewModel Tree { get; } = new DesignSubagentTreeViewModel { Branches = [new DesignOrchestratorCardViewModel()] };

        public bool HasTree => true;

        public string Caption => "1 job";

        public string Footer => "claude-work · 1 job";

        public bool IsOpen => true;

        public IRelayCommand CloseCommand { get; } = new RelayCommand(close);
    }
}

public sealed class SubagentTreeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AWideTreeSpreadsItsFirstLevelSideBySideUnderTheOrchestratorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentTreeViewModel(), ThemeVariant.Dark, 1440, 900);

            Assert.Equal(AgentTreeShape.Tree, view.All<AgentTreePanel>().Single().Layout.Shape);
            Assert.Equal(("6 agents", "2 working", "1 needs you", "1.47 USD"), (view.TextOf("Agents"), view.TextOf("Working"), view.TextOf("NeedsYouText"), view.TextOf("Spent")));
        }, Cancellation);

    [Fact]
    public Task WhoNeedsYouIsHiddenWhenNobodyWaitsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new QuietTree(), ThemeVariant.Dark, 420, 600);

            Assert.Equal((false, false), (view.Shows("NeedsYou"), view.Shows("Working")));
        }, Cancellation);

    private sealed class QuietTree : ISubagentTreeViewModel
    {
        public IReadOnlyList<IAgentBranch> Branches { get; } = [new DesignOrchestratorCardViewModel()];

        public string Agents => "no sub-agent yet";

        public string Working => string.Empty;

        public string NeedsYou => string.Empty;

        public bool HasNeedsYou => false;

        public string Spent => string.Empty;

        public IReadOnlyList<IWaitingAgentViewModel> Waiting { get; } = [];
    }
}

public sealed class BranchPlaceViewScripts(HeadlessUi ui)
{
    private static readonly string[] Marks = ["Spinner", "Waiting", "Completed", "Stopped", "Alert"];

    [Theory]
    [InlineData(SubagentState.Working, "working", "Spinner")]
    [InlineData(SubagentState.Waiting, "waiting", "Waiting")]
    [InlineData(SubagentState.Completed, "completed", "Completed")]
    [InlineData(SubagentState.Stopped, "stopped", "Stopped")]
    [InlineData(SubagentState.Failed, "failed", "Alert")]
    [InlineData(SubagentState.Lost, "lost", "Alert")]
    public Task TheChipSaysTheSubagentsStateWithItsOwnMarkAsync(SubagentState state, string text, string mark) =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignBranchPlaceViewModel("chip", 1, BranchKind.InProcess, state));

            Assert.Equal((text, true), (view.TextOf("StateText"), view.Shows(mark)));
            Assert.Single(Marks, shown => view.Shows(shown));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AWorkingChipSpinsWhileShownAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignBranchPlaceViewModel("chip", 1, BranchKind.InProcess, SubagentState.Working));

            Assert.Equal(MotionLoop.Spin, view.LoopOf("Spinner"));
            Assert.True(view.HasClass("Chip", "working"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AWorkingChipDrawsItsSpinnerInTheAccentColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignBranchPlaceViewModel("chip", 1, BranchKind.InProcess, SubagentState.Working));

            view.Window.TryFindResource("AccentBrush", view.Window.ActualThemeVariant, out var accent);

            Assert.Equal(accent, view.Find<Avalonia.Controls.Shapes.Arc>("Spinner").Stroke);
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationToggleViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheHeaderButtonShowsOnlyForATreeAndIsSelectedWhileThePaneIsOpenAsync() =>
        ui.RunAsync(() =>
        {
            var open = Screen.Show(new DesignDelegationToggleViewModel());
            var hidden = Screen.Show(new DesignDelegationToggleViewModel { IsShown = false });

            Assert.Equal((true, true), (open.Shows("Tree"), open.HasClass("Tree", "selected")));
            Assert.False(hidden.Shows("Tree"));
        }, Cancellation);
}

public sealed class SubagentViewScripts(HeadlessUi ui)
{
    private static Avalonia.Media.Color? Color(Avalonia.Media.IBrush? brush) => (brush as Avalonia.Media.ISolidColorBrush)?.Color;

    [Fact]
    public Task ASubagentIsACardWithItsNameKindActivityMeterAndStateAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentViewModel("Find the refund call sites", "Run go test ./internal/payments/...", SubagentState.Working) { Meter = "2 tool uses · 3,600 tokens" });

            Assert.Equal(
                ("Find the refund call sites", "sub-agent", "Run go test ./internal/payments/...", "2 tool uses · 3,600 tokens", "working"),
                (view.TextOf("AgentName"), view.TextOf("Kind"), view.TextOf("Activity"), view.TextOf("Meter"), view.TextOf("StateText")));
            Assert.Equal((false, 1), (view.Shows("Note"), Graph.GetDepth(view.Named("SubagentView").Single())));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ASubagentWithNoToolsToShowYetReadsItsNameAsBrightlyAsOneThatHasSomeAsync() =>
        ui.RunAsync(() =>
        {
            var quiet = Screen.Show(new DesignSubagentViewModel("Find the refund call sites", "Starting", SubagentState.Working) { ToggleCommand = new RelayCommand(() => { }, () => false) });
            var shown = Screen.Show(new DesignSubagentViewModel("Find the refund call sites", "Starting", SubagentState.Working));

            Assert.Equal(Color(shown.Find<TextBlock>("AgentName").Foreground), Color(quiet.Find<TextBlock>("AgentName").Foreground));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AWaitingSubagentsCardTurnsAttentionAndSaysWhatItAsksAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentViewModel("Install the packages", "Run npm install", SubagentState.Waiting) { Note = "asks: npm install" });

            Assert.Equal(("asks: npm install", true, true), (view.TextOf("Note"), view.HasClass("Card", "attention"), view.HasClass("Note", "waiting")));
            Assert.Equal(EdgeKind.Attention, Graph.GetEdge(view.Named("SubagentView").Single()));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALostSubagentsCardTurnsFailureAndSaysItsWorkWasLostAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentViewModel("Survey the greeting", "Read README.md", SubagentState.Lost) { Note = "its work was lost when its turn or session ended" });

            Assert.Equal((true, true, false), (view.HasClass("Card", "alert"), view.HasClass("Note", "alert"), view.HasClass("Card", "settled")));
            Assert.Equal(EdgeKind.Failure, Graph.GetEdge(view.Named("SubagentView").Single()));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ACompletedSubagentsCardGoesStillAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentViewModel("Plan the work", "Split it into three jobs", SubagentState.Completed));

            Assert.Equal((true, false), (view.HasClass("Card", "settled"), view.Shows("Elapsed")));
            Assert.Equal(EdgeKind.Quiet, Graph.GetEdge(view.Named("SubagentView").Single()));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnOpenSubagentListsTheToolsItRanAsync() =>
        ui.RunAsync(() =>
        {
            var open = Screen.Show(new DesignSubagentViewModel("Find the refund call sites", "Run go test", SubagentState.Working) { Trail = ["Run go test", "Read refund.go"], IsOpen = true });
            var closed = Screen.Show(new DesignSubagentViewModel("Find the refund call sites", "Run go test", SubagentState.Working) { Trail = ["Run go test"] });

            Assert.Equal((true, 2, false), (open.Shows("Trail"), open.Find<ItemsControl>("Trail").ItemCount, closed.Shows("Trail")));
        }, TestContext.Current.CancellationToken);
}

public sealed class WaitingAgentViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnAgentThatNeedsYouSaysWhatItWaitsForAndOpensItsJobWhenChosenAsync() =>
        ui.RunAsync(() =>
        {
            var opened = new List<JobId>();
            var design = new DesignWaitingAgentViewModel();
            var view = Screen.Show(new WaitingAgentViewModel(design.Job, design.Name, design.What, new RelayCommand<JobId>(opened.Add)));

            view.Click("Open");

            Assert.Equal(("Rewrite webhook signature tests", "asks: go generate ./internal/webhooks/..."), (view.TextOf("AgentName"), view.TextOf("What")));
            Assert.Equal([design.Job], opened);
        }, TestContext.Current.CancellationToken);
}
