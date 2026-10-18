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
    public void ALabelSitsOnItsLinkWhereItCoversNoNodeAndNoOtherLabel()
    {
        var route = new LinkRoute([new GraphPoint(80, 0), new GraphPoint(80, 400)]);
        var taken = new List<GraphRect> { new(0, 170, 160, 60) };

        var label = LinkRoutes.Label(route, new GraphSize(60, 20), taken);

        Assert.Equal(new GraphRect(50, 130, 60, 20), label);
    }
}
