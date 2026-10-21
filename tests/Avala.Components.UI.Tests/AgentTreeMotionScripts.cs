using Avala.Components.Graphs;
using Avala.Components.UI.Graphs;
using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Avala.Components.UI.Tests;

public sealed class AgentTreeMotionScripts(HeadlessUi ui)
{
    private static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(240);

    private static readonly TimeSpan Grow = TimeSpan.FromMilliseconds(220);

    private static readonly TimeSpan Arrival = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan Collapse = TimeSpan.FromMilliseconds(220);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ACardMovedByANewSiblingGlidesFromItsOldPlaceToItsNewOneAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);

            tree.Children.Add(Card(1));
            view.Settle();
            var leaving = Seen(tree, 0);
            view.Elapse(Glide / 2);
            var halfway = Seen(tree, 0);
            view.Elapse(Glide);

            Assert.Equal((162d, 243d), (leaving.X, Seen(tree, 0).X));
            Assert.InRange(halfway.X, 200, 242);
        }, Cancellation);

    [Fact]
    public Task TheEdgeOfANewBranchGrowsFromItsParentsJointDownToItsCardAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);

            tree.Children.Add(Card(1));
            view.Settle();
            var sprouting = Drop(tree, 4);
            view.Elapse(Glide);
            view.Elapse(Grow);

            Assert.Empty(sprouting);
            Assert.Equal([new GraphPoint(561, 134), new GraphPoint(561, 148)], Drop(tree, 4));
        }, Cancellation);

    [Fact]
    public Task ANewCardFadesAndRisesIntoPlaceOnceItsEdgeReachesItAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);
            var card = Card(1);

            tree.Children.Add(card);
            view.Settle();
            var arriving = (card.Opacity, Seen(tree, 4).Y);
            view.Elapse(Arrival);

            Assert.Equal((0d, 154d), arriving);
            Assert.Equal((1d, 148d, false), (card.Opacity, Seen(tree, 4).Y, card.IsSet(Visual.OpacityProperty)));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionANewBranchLandsWholeAndInPlaceAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: true);
            var card = Card(1);

            tree.Children.Add(card);
            view.Settle();

            Assert.Equal((1d, new Point(486, 148), new Point(243, 0)), (card.Opacity, Seen(tree, 4), Seen(tree, 0)));
            Assert.Equal([new GraphPoint(561, 134), new GraphPoint(561, 148)], Drop(tree, 4));
        }, Cancellation);

    [Fact]
    public Task AFoldedBranchCollapsesTowardItsParentAndThenIsGoneAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);

            tree.Children.RemoveAt(3);
            view.Settle();
            var folding = tree.Drawn.Leaving.Single();
            view.Elapse(Collapse / 2);
            var shrinking = tree.Drawn.Leaving.Single();
            view.Elapse(Collapse);

            Assert.Equal(new GraphRect(324, 148, 150, 108), folding);
            Assert.True(shrinking.Width < folding.Width && shrinking.Center.X < folding.Center.X && shrinking.Center.Y < folding.Center.Y, $"the folding card stands at {shrinking}");
            Assert.Empty(tree.Drawn.Leaving);
        }, Cancellation);

    [Fact]
    public Task ResizingThePaneMovesTheCardsAtOnceInsteadOfGlidingAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);

            ((Border)tree.Parent!).Width = 220;
            view.Settle();

            Assert.Equal([0d, 24, 24, 24], Enumerable.Range(0, 4).Select(index => Seen(tree, index).X));
        }, Cancellation);

    [Fact]
    public Task TheClockRestsOnceNoEdgeIsWorkingOrWaitingAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);
            view.Elapse(Glide);
            var working = ViewScript.IsAnimating;

            foreach (var card in tree.Children)
            {
                Graph.SetEdge(card, EdgeKind.Quiet);
            }

            view.Settle();
            view.Elapse(Glide);

            Assert.Equal((true, false), (working, ViewScript.IsAnimating));
        }, Cancellation);

    [Fact]
    public Task ACardStillArrivingWhenThePaneIsResizedEndsFullyShownAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);
            var card = Card(1);

            tree.Children.Add(card);
            view.Settle();
            ((Border)tree.Parent!).Width = 780;
            view.Settle();
            view.Elapse(Arrival);
            view.Elapse(Arrival);

            Assert.Equal(1d, card.Opacity);
        }, Cancellation);

    [Fact]
    public Task ABranchThatLeftWhileTheTreeWasHiddenDoesNotCollapseAgainOnceItIsShownAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);
            var holder = (Border)tree.Parent!;

            holder.Child = null;
            view.Settle();
            tree.Children.RemoveAt(3);
            holder.Child = tree;
            view.Settle();

            Assert.Empty(tree.Drawn.Leaving);
        }, Cancellation);

    [Fact]
    public Task ABranchThatLeftWhileItsTreeWasCollapsedDoesNotCollapseAgainOnceTheTreeIsVisibleAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tree) = Shown(reduced: false);
            var holder = (Border)tree.Parent!;

            holder.IsVisible = false;
            view.Settle();
            tree.Children.RemoveAt(3);
            view.Settle();
            holder.IsVisible = true;
            view.Settle();

            Assert.Empty(tree.Drawn.Leaving);
        }, Cancellation);

    private static (ViewScript View, AgentTreePanel Tree) Shown(bool reduced)
    {
        var tree = new AgentTreePanel();
        tree.Children.AddRange([Card(0, 120), Card(1), Card(1), Card(1)]);
        var view = ViewScript.Show(new Border(), new object());
        Motion.SetIsReduced(view.Window, reduced);
        view.Window.Width = 800;
        view.Window.Content = new Border { Width = 800, HorizontalAlignment = HorizontalAlignment.Left, Child = tree };

        return (view.Settle(), tree);
    }

    private static Point Seen(AgentTreePanel tree, int index) => tree.Children[index].TranslatePoint(default, tree) ?? default;

    private static IReadOnlyList<GraphPoint> Drop(AgentTreePanel tree, int node) =>
        tree.Drawn.Edges.Where(edge => edge.Node == node).Select(edge => edge.Points).SingleOrDefault() ?? [];

    private static Border Card(int depth, double height = 108)
    {
        var card = new Border { Width = 150, Height = height };
        Graph.SetDepth(card, depth);
        Graph.SetEdge(card, EdgeKind.Flowing);

        return card;
    }
}
