using Avala.Components.Graphs;
using Avala.Components.UI.Graphs;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class AgentTreePanelAdversaryScripts(HeadlessUi ui)
{
    private const string Words = "Rewrite the webhook signature verification suite against the new provider contract";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ACardIndentedPastThePanesWidthStillStaysInsideThePaneAsync() =>
        ui.RunAsync(() =>
        {
            var tree = Chain(7);
            ViewScript.Show(new Border { Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Child = tree }, new object());

            Assert.Equal(AgentTreeShape.Rail, tree.Layout.Shape);
            Assert.All(tree.Children, card => Assert.True(card.Bounds.Right <= 120, $"a card at depth {Graph.Read(card, Graph.DepthProperty)} ends at {card.Bounds.Right}"));
        }, Cancellation);

    [Fact]
    public Task APanelArrangedNarrowerThanItWasMeasuredSettlesWithEveryCardInsideItAsync() =>
        ui.RunAsync(() =>
        {
            var tree = Chain(4);
            var view = ViewScript.Show(new MeasuredWide { Child = tree }, new object());
            view.Settle();

            Assert.True(tree.IsMeasureValid && tree.IsArrangeValid, "the panel never settles");
            Assert.All(tree.Children, card => Assert.True(card.Bounds.Bottom <= tree.Bounds.Height, $"a card ends at {card.Bounds.Bottom}, below the panel's {tree.Bounds.Height}"));
        }, Cancellation);

    private static AgentTreePanel Chain(int depth)
    {
        var tree = new AgentTreePanel { CardWidth = 150, RootWidth = 150 };

        for (var level = 0; level < depth; level++)
        {
            var card = new Border { Child = new TextBlock { Text = Words, TextWrapping = TextWrapping.Wrap } };
            Graph.SetDepth(card, level);
            Graph.SetEdge(card, EdgeKind.Flowing);
            tree.Children.Add(card);
        }

        return tree;
    }

    private sealed class MeasuredWide : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            Child?.Measure(new Size(1000, double.PositiveInfinity));

            return new Size(160, Child?.DesiredSize.Height ?? 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(0, 0, 160, finalSize.Height));

            return finalSize;
        }
    }
}
