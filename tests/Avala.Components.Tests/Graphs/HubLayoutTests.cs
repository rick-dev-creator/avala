using Avala.Components.Graphs;

namespace Avala.Components.Tests.Graphs;

public sealed class HubLayoutTests
{
    private static readonly GraphSize Node = new(210, 40);

    [Fact]
    public void NoHubsTakeNoRoom() =>
        Assert.Equal(HubGraphLayout.Empty, HubLayout.Arrange([], new HubSpacing()));

    [Fact]
    public void TheBusiestHubsTakeTheSidesAndTheirSatellitesFaceOutward()
    {
        var layout = HubLayout.Arrange([Hub(64, 6), Hub(64, 2), Hub(46, 1), Hub(46, 1)], new HubSpacing());
        var (work, personal, codex, pi) = (layout.Hubs[0], layout.Hubs[1], layout.Hubs[2], layout.Hubs[3]);

        Assert.True(work.Center.X < codex.Center.X && codex.Center.X < personal.Center.X);
        Assert.True(codex.Center.Y < work.Center.Y && work.Center.Y < pi.Center.Y);
        Assert.All(work.Satellites.Take(1), rect => Assert.True(rect.Center.X < work.Center.X));
        Assert.All(personal.Satellites, rect => Assert.True(rect.Center.X > personal.Center.X));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void AnOddNumberOfHubsStandsMirroredAboutTheMiddleOfThePage(int hubs)
    {
        var layout = HubLayout.Arrange([.. Enumerable.Range(0, hubs).Select(_ => Hub(64, 0))], new HubSpacing());
        var offsets = layout.Hubs.Select(hub => Math.Round(hub.Center.X - (layout.Size.Width / 2), 6)).Order().ToList();

        Assert.Equal(offsets, offsets.Select(offset => -offset).Order());
    }

    [Theory]
    [InlineData(1, 12)]
    [InlineData(4, 6)]
    [InlineData(12, 8)]
    public void NoSatelliteOverlapsAnotherOrAHubHoweverManyThereAre(int hubs, int satellites)
    {
        var layout = HubLayout.Arrange([.. Enumerable.Range(0, hubs).Select(_ => Hub(64, satellites))], new HubSpacing());
        var rects = layout.Hubs.SelectMany(hub => hub.Satellites)
            .Concat(layout.Hubs.Select(hub => GraphRect.Around(hub.Center, new GraphSize(hub.Radius * 2, hub.Radius * 2))))
            .ToList();

        Assert.Equal(hubs * satellites, layout.Hubs.Sum(hub => hub.Satellites.Count));
        Assert.DoesNotContain(rects.SelectMany((first, index) => rects.Skip(index + 1).Select(second => (first, second))), pair => pair.first.Overlaps(pair.second, 0));
        Assert.All(rects, rect => Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.Right <= layout.Size.Width && rect.Bottom <= layout.Size.Height));
    }

    [Fact]
    public void ASatelliteArrivingLaterLeavesTheOthersWhereTheyWere()
    {
        var before = HubLayout.Arrange([Hub(64, 3)], new HubSpacing());
        var after = HubLayout.Arrange([Hub(64, 4)], new HubSpacing());

        Assert.Equal(
            before.Hubs[0].Satellites.Select(rect => Relative(rect, before.Hubs[0])),
            after.Hubs[0].Satellites.Take(3).Select(rect => Relative(rect, after.Hubs[0])));
    }

    [Fact]
    public void TheGraphIsSizedAndCentredOnWhatItDraws()
    {
        var spacing = new HubSpacing();
        var layout = HubLayout.Arrange([Hub(64, 5), Hub(64, 0)], spacing);
        var drawn = Drawn(layout).ToList();

        Assert.Equal(
            (spacing.Padding, spacing.Padding, layout.Size.Width - spacing.Padding, layout.Size.Height - spacing.Padding),
            (drawn.Min(rect => rect.X), drawn.Min(rect => rect.Y), drawn.Max(rect => rect.Right), drawn.Max(rect => rect.Bottom)));
    }

    [Fact]
    public void AGraphGrowingFromItsLastFrameKeepsItsHubsWhereTheyWereOnACentredPage()
    {
        var spacing = new HubSpacing();
        var before = HubLayout.Arrange([Hub(64, 2), Hub(46, 0)], spacing);
        var after = HubLayout.Arrange([Hub(64, 5), Hub(46, 1)], spacing, before.Frame);

        Assert.Equal(
            before.Hubs.Select(hub => OnPage(hub, before)),
            after.Hubs.Select(hub => OnPage(hub, after)));
        Assert.True(after.Size.Width >= before.Size.Width && after.Size.Height >= before.Size.Height);
    }

    public static TheoryData<double[], int[]> Graphs => new()
    {
        { [64, 64, 46], [2, 2, 1] },
        { [64, 64, 64, 46, 46], [6, 5, 4, 3, 2] },
        { [64], [12] },
    };

    [Theory]
    [MemberData(nameof(Graphs))]
    public void NoEdgeCrossesANodeOtherThanItsOwnEnds(double[] radii, int[] satellites)
    {
        var layout = HubLayout.Arrange([.. radii.Select((radius, index) => Hub(radius, satellites[index]))], new HubSpacing());
        var edges = layout.Hubs.SelectMany(hub => hub.Satellites.Select(rect => (Hub: hub, Satellite: rect, Curve: HubLayout.Edge(hub.Center, hub.Radius, rect))));
        var crossings = edges.SelectMany(edge => Drawn(layout)
            .Where(rect => rect != edge.Satellite && rect != Square(edge.Hub))
            .Where(rect => Enumerable.Range(0, 4097).Select(step => edge.Curve.At(step / 4096.0)).Any(point => point.X > rect.X && point.X < rect.Right && point.Y > rect.Y && point.Y < rect.Bottom))
            .Select(rect => (edge.Satellite, rect)));

        Assert.Empty(crossings);
    }

    [Fact]
    public void TheAnglesOfASatelliteDoNotDependOnHowManyFollowIt() =>
        Assert.Equal(HubLayout.Angles(3, 180, false), HubLayout.Angles(7, 180, false).Take(3));

    private static (double X, double Y) Relative(GraphRect rect, HubPlacement hub) =>
        (Math.Round(rect.X - hub.Center.X, 6), Math.Round(rect.Y - hub.Center.Y, 6));

    private static (double X, double Y) OnPage(HubPlacement hub, HubGraphLayout layout) =>
        (Math.Round(hub.Center.X - (layout.Size.Width / 2), 6), Math.Round(hub.Center.Y - (layout.Size.Height / 2), 6));

    private static IEnumerable<GraphRect> Drawn(HubGraphLayout layout) =>
        layout.Hubs.Select(Square).Concat(layout.Hubs.SelectMany(hub => hub.Satellites));

    private static GraphRect Square(HubPlacement hub) => GraphRect.Around(hub.Center, new GraphSize(hub.Radius * 2, hub.Radius * 2));

    private static HubSpec Hub(double radius, int satellites) => new(radius, [.. Enumerable.Repeat(Node, satellites)]);
}
