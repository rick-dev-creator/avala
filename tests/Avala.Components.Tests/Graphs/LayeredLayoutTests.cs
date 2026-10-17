using Avala.Components.Graphs;

namespace Avala.Components.Tests.Graphs;

public sealed class LayeredLayoutTests
{
    private static readonly GraphSize Box = new(160, 60);

    private static readonly LayeredSpacing Spacing = new(ColumnGap: 40, RowGap: 50, GroupPadding: 20, GroupLabel: 20);

    [Fact]
    public void AnEmptyGraphTakesNoRoom() =>
        Assert.Equal(LayeredGraphLayout.Empty, LayeredLayout.Arrange([], [], 0, Spacing));

    [Fact]
    public void EachNodeSitsOneRowBelowTheDeepestNodeThatLeadsToItAndACycleStillEnds() =>
        Assert.Equal([0, 1, 2, 1, 3], LayeredLayout.Ranks(5, [new(0, 1), new(1, 2), new(0, 3), new(3, 2), new(2, 4), new(4, 2)]));

    [Fact]
    public void RowsStackDownwardAndAShorterRowIsCentredUnderAWiderOne()
    {
        var layout = LayeredLayout.Arrange([Node(), Node(), Node()], [new(0, 2), new(1, 2)], 0, Spacing);

        Assert.Equal([(0d, 0d), (200d, 0d), (100d, 110d)], layout.Nodes.Select(node => (node.X, node.Y)));
        Assert.Equal(new GraphSize(360, 170), layout.Size);
    }

    [Fact]
    public void AGroupFramesItsMembersWithRoomForItsLabelAndTheLayoutMakesRoomForTheFrame()
    {
        var layout = LayeredLayout.Arrange([Node(), Node(group: 0), Node(group: 0)], [new(0, 1), new(0, 2)], 1, Spacing);

        var frame = layout.Groups[0];
        Assert.All(layout.Nodes.Skip(1), node => Assert.True(node.X >= frame.X && node.Right <= frame.Right && node.Y >= frame.Y && node.Bottom <= frame.Bottom - 20));
        Assert.True(layout.Nodes.Concat(layout.Groups).All(rect => rect.X >= 0 && rect.Y >= 0));
    }

    [Fact]
    public void MembersOfAGroupStayNextToEachOtherInTheirRow()
    {
        var layout = LayeredLayout.Arrange([Node(), Node(group: 0), Node(), Node(group: 0)], [new(0, 1), new(0, 2), new(0, 3)], 1, Spacing);

        var xs = layout.Nodes.Select(node => node.X).ToList();
        Assert.Equal(Box.Width + Spacing.ColumnGap, Math.Abs(xs[1] - xs[3]));
    }

    [Fact]
    public void ANodeOutsideAGroupNeverFallsInsideItsFrame()
    {
        var layout = LayeredLayout.Arrange([Node(), Node(group: 0), Node(), Node(group: 0), Node(group: 0)], [new(0, 1), new(1, 2), new(1, 3), new(3, 4)], 1, Spacing);

        var frame = layout.Groups[0];
        var outsider = layout.Nodes[2];
        Assert.True(outsider.Right <= frame.X || outsider.X >= frame.Right || outsider.Bottom <= frame.Y || outsider.Y >= frame.Bottom);
    }

    private static LayeredNodeSpec Node(int group = -1) => new(Box) { Group = group };
}
