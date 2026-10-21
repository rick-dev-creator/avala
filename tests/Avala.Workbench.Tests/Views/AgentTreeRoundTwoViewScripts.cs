using Avala.Components.Status;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avala.Components.UI.Graphs;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class AgentTreeRoundTwoViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task EveryCardSixLevelsDownInAVeryNarrowPaneStillShowsItsWholeStateAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new AgentTreeSamples.Pane(new DesignSubagentTreeViewModel { Branches = Chain(6) }), ThemeVariant.Dark, 260, 1600);

            var cut = view.All<Border>().Where(card => card.Name == "Card")
                .Select(card => (Card: card, Chip: card.GetVisualDescendants().OfType<Border>().Single(chip => chip.Name == "Chip")))
                .Select(pair => (
                    CardRight: pair.Card.TranslatePoint(new Point(pair.Card.Bounds.Width - pair.Card.Padding.Right, 0), view.Window)?.X ?? 0,
                    ChipRight: pair.Chip.TranslatePoint(new Point(pair.Chip.Bounds.Width, 0), view.Window)?.X ?? 0,
                    pair.Chip.Bounds.Width))
                .Where(shown => shown.ChipRight > shown.CardRight + 0.5 || shown.Width < 1)
                .ToList();

            Assert.Empty(cut);
        }, Cancellation);

    [Fact]
    public Task OverviewHangsATreeTooWideForTheWindowOnRailsInsteadOfScrollingItSidewaysAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationViewModel(), ThemeVariant.Dark, 1024, 900);
            var canvas = view.Find<ScrollViewer>("Canvas");
            var panel = view.All<AgentTreePanel>()[0];

            Assert.True(canvas.Extent.Width <= canvas.Viewport.Width + 0.5, $"the tree is {panel.Bounds.Width} wide in a {canvas.Viewport.Width} window, laid out as {panel.Layout.Shape}");
        }, Cancellation);

    [Fact]
    public Task TheOrchestratorCardSaysItIsTheOrchestratorInTheSidePaneAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new AgentTreeSamples.Pane(new DesignSubagentTreeViewModel { Branches = [new DesignOrchestratorCardViewModel()] }), ThemeVariant.Dark, 380, 900);
            var card = view.Find<Border>("Card");
            var source = view.Find<TextBlock>("Source");
            var cardRight = card.TranslatePoint(new Point(card.Bounds.Width - card.Padding.Right, 0), view.Window)?.X ?? 0;
            var sourceRight = source.TranslatePoint(new Point(source.DesiredSize.Width, 0), view.Window)?.X ?? 0;

            Assert.True(sourceRight <= cardRight + 0.5, $"'{string.Concat(source.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text) ?? [])}' ends at {sourceRight}, past the card's {cardRight}");
        }, Cancellation);

    [Fact]
    public Task AChildJobsInstanceIsReadInFullInTheSidePaneAsync() =>
        ui.RunAsync(() =>
        {
            var child = new DesignDelegationNodeViewModel(JobId.New(), "Use the Agent tool to launch one general-purpose sub-agent", 1, StatusKind.Working, "Claude Code", "claude-cuenta2", "working", "0.10 USD", false, 0)
            {
                Place = new DesignBranchPlaceViewModel("child", 1, BranchKind.Child, SubagentState.Working),
            };
            var view = Screen.Show(new AgentTreeSamples.Pane(new DesignSubagentTreeViewModel { Branches = [new DesignOrchestratorCardViewModel(), child] }), ThemeVariant.Dark, 380, 900);
            var connection = view.Find<TextBlock>("Connection");

            Assert.False(connection.TextLayout.TextLines.Any(line => line.HasCollapsed), $"'{connection.Text}' is cut to {connection.Bounds.Width} wide");
        }, Cancellation);

    private static IReadOnlyList<IAgentBranch> Chain(int depth) => AgentTreeRoundTwoViewScriptsChain.Of(depth);
}

internal static class AgentTreeRoundTwoViewScriptsChain
{
    public static IReadOnlyList<IAgentBranch> Of(int depth) =>
    [
        new DesignOrchestratorCardViewModel(),
        .. Enumerable.Range(1, depth).Select(level => (IAgentBranch)new DesignDelegationNodeViewModel(JobId.New(), $"Level {level} job", level, StatusKind.Working, "Claude Code", "claude-work", "Doing things", "0.10 USD", false, 0)
        {
            Place = new DesignBranchPlaceViewModel($"level-{level}", level, BranchKind.Child, SubagentState.Waiting) { Branches = 1 },
        }),
    ];
}
