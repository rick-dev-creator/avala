using Avala.Components.Graphs;
using Avala.Components.UI.Graphs;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Avala.Components.UI.Tests;

public sealed class AgentTreePanelScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task WithRoomTheSubAgentsStandSideBySideUnderTheOrchestratorAsync() =>
        ui.RunAsync(() =>
        {
            var tree = Tree();
            ViewScript.Show(tree, new object());

            Assert.Equal(AgentTreeShape.Comb, tree.Layout.Shape);
            Assert.Equal([148d, 148, 148], tree.Children.Skip(1).Select(card => card.Bounds.Y));
            Assert.Equal([0d, 162, 324], tree.Children.Skip(1).Select(card => card.Bounds.X));
            Assert.Equal(162, tree.Children[0].Bounds.X);
        }, Cancellation);

    [Fact]
    public Task InANarrowPaneEverySubAgentHangsOnARailUnderTheOrchestratorAsync() =>
        ui.RunAsync(() =>
        {
            var tree = Tree();
            ViewScript.Show(new Border { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Child = tree }, new object());

            Assert.Equal(AgentTreeShape.Rail, tree.Layout.Shape);
            Assert.Equal([0d, 24, 24, 24], tree.Children.Select(card => card.Bounds.X));
            Assert.Equal([0d, 132, 252, 372], tree.Children.Select(card => card.Bounds.Y));
        }, Cancellation);

    [Fact]
    public Task ACardThatGrowsPushesTheCardsBelowItDownAsync() =>
        ui.RunAsync(() =>
        {
            var tree = Tree();
            var view = ViewScript.Show(new Border { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Child = tree }, new object());

            ((Border)tree.Children[1]).Height = 200;
            view.Settle();

            Assert.Equal([0d, 132, 344, 464], tree.Children.Select(card => card.Bounds.Y));
        }, Cancellation);

    [Fact]
    public Task ATreeAlmostWideEnoughNarrowsItsCardsToStaySideBySideBeforeFallingToRailsAsync() =>
        ui.RunAsync(() =>
        {
            var tree = Sized(280);
            var narrow = Sized(280);
            ViewScript.Show(new Border { Width = 800, HorizontalAlignment = HorizontalAlignment.Left, Child = tree }, new object());
            ViewScript.Show(new Border { Width = 560, HorizontalAlignment = HorizontalAlignment.Left, Child = narrow }, new object());

            Assert.Equal(AgentTreeShape.Comb, tree.Layout.Shape);
            Assert.All(tree.Children.Skip(1), card => Assert.True(card.Bounds.Width is >= 200 and < 280, $"a card {card.Bounds.Width} wide"));
            Assert.True(tree.Children.Max(card => card.Bounds.Right) <= 800);
            Assert.Equal(AgentTreeShape.Rail, narrow.Layout.Shape);
        }, Cancellation);

    private static AgentTreePanel Sized(double width)
    {
        var tree = new AgentTreePanel { CardWidth = width, RootWidth = width };
        tree.Children.AddRange([Sizable(0), Sizable(1), Sizable(1), Sizable(1)]);

        return tree;
    }

    private static Border Sizable(int depth)
    {
        var card = new Border { Height = 100 };
        Graph.SetDepth(card, depth);
        Graph.SetEdge(card, EdgeKind.Flowing);

        return card;
    }

    private static AgentTreePanel Tree()
    {
        var tree = new AgentTreePanel();
        tree.Children.AddRange([Card(0, 120), Card(1, 108), Card(1, 108), Card(1, 108)]);

        return tree;
    }

    private static Border Card(int depth, double height)
    {
        var card = new Border { Width = 150, Height = height };
        Graph.SetDepth(card, depth);
        Graph.SetEdge(card, EdgeKind.Flowing);

        return card;
    }
}
