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
    public void ACallAndItsReplyThatGoAroundANodeNeverCrossEachOther()
    {
        var links = new[] { new GraphLink("a", "c", "Asks", EdgeKind.Quiet), new GraphLink("c", "a", "Answers", EdgeKind.Quiet) };
        var routes = links.Select(link => (Link: link, Shift: LinkLanes.Shift(links, link, link.From == "a" ? (80, 30) : (80, 270), link.From == "a" ? (80, 270) : (80, 30))))
            .Select(pair => LinkRoutes.Route(pair.Link.From == "a" ? Top : Bottom, pair.Link.From == "a" ? Bottom : Top, [Top, Middle, Bottom], new GraphPoint(pair.Shift.X, pair.Shift.Y)).Points)
            .ToList();

        var crossings = Segments(routes[0]).SelectMany(one => Segments(routes[1]).Where(other => Cross(one, other)).Select(other => $"{one} crosses {other}")).ToList();

        Assert.Empty(crossings);
    }

    [Fact]
    public void TwoLinksBetweenADiagonalPairThatGoAroundANodeKeepTheirOwnLanes()
    {
        GraphRect from = new(0, 0, 100, 100), between = new(150, 150, 100, 100), to = new(300, 300, 100, 100);
        var links = new[] { new GraphLink("a", "b", "First", EdgeKind.Quiet), new GraphLink("a", "b", "Second", EdgeKind.Quiet) };

        var routes = links.Select(link => LinkLanes.Shift(links, link, (50, 50), (350, 350))).Select(shift => LinkRoutes.Route(from, to, [from, between, to], new GraphPoint(shift.X, shift.Y)).Points).ToList();

        Assert.NotEqual(routes[0], routes[1]);
    }

    [Fact]
    public void SixLinksBetweenOnePairThatGoAroundANodeNeverRunOnTopOfEachOther()
    {
        var links = Enumerable.Range(0, 6).Select(index => new GraphLink(index % 2 == 0 ? "a" : "c", index % 2 == 0 ? "c" : "a", $"Hop {index}", EdgeKind.Quiet)).ToList();
        var routes = links.Select(link => (Link: link, Shift: LinkLanes.Shift(links, link, link.From == "a" ? (80, 30) : (80, 270), link.From == "a" ? (80, 270) : (80, 30))))
            .Select(pair => LinkRoutes.Route(pair.Link.From == "a" ? Top : Bottom, pair.Link.From == "a" ? Bottom : Top, [Top, Middle, Bottom], new GraphPoint(pair.Shift.X, pair.Shift.Y)).Points)
            .ToList();

        var shared = routes.SelectMany((one, index) => routes.Skip(index + 1).SelectMany(other => Segments(one).SelectMany(segment => Segments(other).Where(next => Shared(segment, next) > 1).Select(next => $"{segment} runs on {next}")))).ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void ALinkThatRunsStraightNeverCrossesALinkOfTheSamePairThatGoesAroundANode()
    {
        GraphRect from = new(0, 0, 160, 60), to = new(600, -60, 160, 60), above = new(330, -210, 100, 200), below = new(300, 80, 100, 60);
        var links = new[] { new GraphLink("a", "b", "Asks", EdgeKind.Quiet), new GraphLink("a", "b", "Asks again", EdgeKind.Quiet) };

        var routes = links.Select(link => LinkLanes.Shift(links, link, (80, 30), (680, -30))).Select(shift => LinkRoutes.Route(from, to, [from, to, above, below], new GraphPoint(shift.X, shift.Y)).Points).ToList();
        var crossings = Segments(routes[0]).SelectMany(one => Segments(routes[1]).Where(other => Cross(one, other)).Select(other => $"{one} crosses {other}")).ToList();

        Assert.Empty(crossings);
    }

    [Fact]
    public void EveryLabelOfThreeLinksBetweenOnePairStaysWithinItsOwnWidthOfItsLinkAndClearOfTheOthers()
    {
        var links = new[] { new GraphLink("a", "b", "Stores", EdgeKind.Quiet), new GraphLink("a", "b", "Sends events", EdgeKind.Quiet), new GraphLink("a", "b", "Writes orders to", EdgeKind.Quiet) };
        var taken = new List<GraphRect> { Top, Middle, Bottom };
        var labels = new List<GraphRect>();
        var distances = new List<string>();

        foreach (var link in links)
        {
            var shift = LinkLanes.Shift(links, link, (80, 30), (80, 150));
            var route = LinkRoutes.Route(Top, Middle, [Top, Middle, Bottom], new GraphPoint(shift.X, shift.Y));
            var label = LinkRoutes.Label(route, new GraphSize(100, 20), taken);
            taken.Add(label);
            labels.Add(label);
            var gap = Math.Max(0, Math.Max(label.X - route.Points.Max(point => point.X), route.Points.Min(point => point.X) - label.Right));
            distances.Add($"{link.Label} sits {gap} beside its link");
        }

        Assert.All(distances, distance => Assert.Matches(" sits [0-9]{1,2}(\\.[0-9]+)? beside", distance));
        Assert.All(labels, label => Assert.DoesNotContain(labels, other => other != label && other.Overlaps(label, 0)));
    }

    private static double Shared((GraphPoint A, GraphPoint B) one, (GraphPoint A, GraphPoint B) other)
    {
        static double Side(GraphPoint p, GraphPoint q, GraphPoint r) => ((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X));

        var length = LinkRoutes.Distance(one.A, one.B);

        if (length < 1e-6 || Math.Abs(Side(one.A, one.B, other.A)) > 1e-6 || Math.Abs(Side(one.A, one.B, other.B)) > 1e-6)
        {
            return 0;
        }

        double Along(GraphPoint point) => (((point.X - one.A.X) * (one.B.X - one.A.X)) + ((point.Y - one.A.Y) * (one.B.Y - one.A.Y))) / length;
        var (start, end) = (Math.Min(Along(other.A), Along(other.B)), Math.Max(Along(other.A), Along(other.B)));

        return Math.Max(0, Math.Min(length, end) - Math.Max(0, start));
    }

    private static IEnumerable<(GraphPoint, GraphPoint)> Segments(IReadOnlyList<GraphPoint> points) => points.Zip(points.Skip(1));

    private static bool Cross((GraphPoint A, GraphPoint B) one, (GraphPoint A, GraphPoint B) other)
    {
        static double Side(GraphPoint p, GraphPoint q, GraphPoint r) => Math.Sign(((q.X - p.X) * (r.Y - p.Y)) - ((q.Y - p.Y) * (r.X - p.X)));

        return Side(one.A, one.B, other.A) * Side(one.A, one.B, other.B) < 0 && Side(other.A, other.B, one.A) * Side(other.A, other.B, one.B) < 0;
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
