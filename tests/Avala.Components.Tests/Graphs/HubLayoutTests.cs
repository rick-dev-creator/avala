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
            before.Hubs[0].Satellites.Select(rect => (rect.X - before.Hubs[0].Center.X, rect.Y - before.Hubs[0].Center.Y)),
            after.Hubs[0].Satellites.Take(3).Select(rect => (rect.X - after.Hubs[0].Center.X, rect.Y - after.Hubs[0].Center.Y)));
    }

    [Fact]
    public void TheRingStaysCentredWhateverSideTheSatellitesGrowOn()
    {
        var layout = HubLayout.Arrange([Hub(64, 5), Hub(64, 0)], new HubSpacing());

        Assert.Equal(layout.Size.Width / 2, (layout.Hubs[0].Center.X + layout.Hubs[1].Center.X) / 2, 3);
    }

    [Fact]
    public void TheAnglesOfASatelliteDoNotDependOnHowManyFollowIt() =>
        Assert.Equal(HubLayout.Angles(3, 180, false), HubLayout.Angles(7, 180, false).Take(3));

    private static HubSpec Hub(double radius, int satellites) => new(radius, [.. Enumerable.Repeat(Node, satellites)]);
}
