using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class DelegationPaneViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePaneShowsTheRootAboveItsChildrenWithTheViewedJobMarkedAndSubagentsAsDashedPillsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationPaneViewModel(), ThemeVariant.Dark, 420, 900);
            var root = view.Find("Root");
            var children = view.Find("Children");

            Assert.True(root.TranslatePoint(default, view.Window)?.Y < children.TranslatePoint(default, view.Window)?.Y);
            Assert.Equal(("3 jobs · 3 agents", "claude-work · 2 jobs   codex-team · 1 job"), (view.TextOf("Caption"), view.TextOf("Footer")));
            Assert.Equal(3, view.Named("SubagentView").Count);
            Assert.All(view.All<Rectangle>().Where(outline => outline.Name == "Outline"), outline => Assert.NotNull(outline.StrokeDashArray));
            Assert.Equal(1, view.All<Border>().Count(card => card.Name == "Card" && card.Classes.Contains("viewing")));
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
        public IOrchestratorCardViewModel? Root { get; } = new DesignOrchestratorCardViewModel();

        public IReadOnlyList<IDelegationNodeViewModel> Children { get; } = [];

        public string Caption => "1 job";

        public string Footer => "claude-work · 1 job";

        public bool IsOpen => true;

        public IRelayCommand CloseCommand { get; } = new RelayCommand(close);
    }
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
    [Fact]
    public Task ASubagentIsADashedPillWithItsNameActivityMeterAndStateAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSubagentViewModel("Read the signature tests", "Waiting for your answer", SubagentState.Waiting) { Meter = "2 tool uses · 3,600 tokens" });
            var quiet = Screen.Show(new DesignSubagentViewModel("Plan the work", "Split it into three jobs", SubagentState.Done));

            Assert.Equal(("Read the signature tests", "Waiting for your answer", "2 tool uses · 3,600 tokens", "waiting"), (view.TextOf("AgentName"), view.TextOf("Activity"), view.TextOf("Meter"), view.TextOf("State")));
            Assert.False(quiet.Shows("Meter"));
            Assert.Equal((true, true), (view.HasClass("Outline", "waiting"), view.HasClass("Dot", "waiting")));
            Assert.NotNull(view.Find<Rectangle>("Outline").StrokeDashArray);
        }, TestContext.Current.CancellationToken);
}
