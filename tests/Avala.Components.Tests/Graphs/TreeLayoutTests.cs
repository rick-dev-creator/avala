using Avala.Components.Graphs;

namespace Avala.Components.Tests.Graphs;

public sealed class TreeLayoutTests
{
    private static readonly GraphSize Card = new(380, 110);

    [Fact]
    public void AnEmptyTreeTakesNoRoom() =>
        Assert.Equal(TreeGraphLayout.Empty, TreeLayout.Arrange([], new TreeSpacing()));

    [Fact]
    public void EachNodeHangsFromTheNearestShallowerNodeBeforeIt() =>
        Assert.Equal([-1, 0, 1, 0, -1], TreeLayout.Parents([Spec(1), Spec(2), Spec(3), Spec(2), Spec(1)]));

    [Fact]
    public void ChildrenStackInTheirDepthsColumnAndTheRootSitsAtTheMiddle()
    {
        var layout = TreeLayout.Arrange([Spec(1), Spec(1), Spec(1)], new TreeSpacing(Lead: 240, ColumnGap: 64, RowGap: 20));

        Assert.All(layout.Nodes, node => Assert.Equal(240, node.Bounds.X));
        Assert.Equal([0d, 130, 260], layout.Nodes.Select(node => node.Bounds.Y));
        Assert.Equal((0d, 185d, 370d), (layout.Root.X, layout.Root.Y, layout.Size.Height));
    }

    [Fact]
    public void AParentIsCentredOnItsChildrenAndTheRootStaysInTheMiddleOfAnUnevenTree()
    {
        var layout = TreeLayout.Arrange([Spec(1), Spec(2), Spec(2), Spec(2)], new TreeSpacing(Lead: 240, ColumnGap: 64, RowGap: 20));

        Assert.Equal(240 + 380 + 64, layout.Nodes[1].Bounds.X);
        Assert.Equal(layout.Nodes[2].Bounds.Center.Y, layout.Nodes[0].Bounds.Center.Y, 3);
        Assert.Equal(layout.Size.Height / 2, layout.Root.Y, 3);
    }

    [Fact]
    public void AnOrphanDepthIsTreatedAsAFirstLevelChild() =>
        Assert.Equal([-1, -1], TreeLayout.Parents([Spec(3), Spec(0)]));

    private static TreeNodeSpec Spec(int depth) => new(depth, Card);
}
