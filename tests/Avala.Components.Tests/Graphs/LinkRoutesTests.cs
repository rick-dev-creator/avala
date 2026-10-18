using Avala.Components.Graphs;

namespace Avala.Components.Tests.Graphs;

public sealed class LinkRoutesTests
{
    private static readonly GraphRect Top = new(0, 0, 160, 60);
    private static readonly GraphRect Middle = new(0, 120, 160, 60);
    private static readonly GraphRect Bottom = new(0, 240, 160, 60);
    private static readonly GraphRect Aside = new(260, 120, 160, 60);

    [Fact]
    public void ALinkThatCrossesNoOtherNodeRunsStraightFromEdgeToEdge() =>
        Assert.Equal([new GraphPoint(80, 60), new GraphPoint(80, 120)], LinkRoutes.Route(Top, Middle, [Top, Middle, Bottom], default).Points);

    [Fact]
    public void ALinkThatWouldCrossANodeGoesAroundItAlongTheShortestClearLane() =>
        Assert.Equal(
            [new GraphPoint(0, 270), new GraphPoint(-LinkRoutes.LaneGap, 270), new GraphPoint(-LinkRoutes.LaneGap, 30), new GraphPoint(0, 30)],
            LinkRoutes.Route(Bottom, Top, [Top, Middle, Bottom, Aside], default).Points);

    [Fact]
    public void TwoLinksBetweenTheSamePairThatGoAroundANodeKeepTheirOwnLanes() =>
        Assert.NotEqual(
            LinkRoutes.Route(Bottom, Top, [Top, Middle, Bottom], new GraphPoint(-9, 0)).Points,
            LinkRoutes.Route(Bottom, Top, [Top, Middle, Bottom], new GraphPoint(9, 0)).Points);

    [Fact]
    public void ALinkInItsOwnLaneAroundANodeStillLeavesAndReachesTheEdgesOfItsNodes()
    {
        var points = LinkRoutes.Route(Bottom, Top, [Top, Middle, Bottom], new GraphPoint(-9, 0)).Points;

        Assert.Contains(points[0].X, new[] { Bottom.X, Bottom.Right });
        Assert.Contains(points[^1].X, new[] { Top.X, Top.Right });
    }

    [Fact]
    public void EveryLaneOfThreeLinksAroundANodeLiesOutsideTheFrameAroundTheirNodes()
    {
        var padding = new LayeredSpacing().GroupPadding;

        var lanes = new[] { -LinkLanes.Gap, 0, LinkLanes.Gap }.Select(shift => LinkRoutes.Route(Bottom, Top, [Top, Middle, Bottom], new GraphPoint(shift, 0)).Points[1].X).ToList();

        Assert.All(lanes, lane => Assert.True(lane > Top.Right + padding || lane < Top.X - padding, $"the lane at {lane} runs inside the frame from {Top.X - padding} to {Top.Right + padding}"));
    }

    [Fact]
    public void ALabelSitsOnItsLinkWhereItCoversNoNodeAndNoOtherLabel()
    {
        var route = new LinkRoute([new GraphPoint(80, 0), new GraphPoint(80, 400)]);
        var taken = new List<GraphRect> { new(0, 170, 160, 60) };

        var label = LinkRoutes.Label(route, new GraphSize(60, 20), taken);

        Assert.Equal(new GraphRect(50, 130, 60, 20), label);
    }
}
