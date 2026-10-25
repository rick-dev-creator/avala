using Avala.Components.Graphs;

namespace Avala.Components.Tests.Graphs;

public sealed class RelayLayoutTests
{
    private static readonly RelaySpacing Spacing = new(64, 18, 56);

    private static readonly RelayCourse Relayed = new(
        2,
        [new RelayStretch(0, 0, 0.5, RelayStretchKind.Ran), new RelayStretch(1, 0.5, 1, RelayStretchKind.Running)],
        [new RelayStop(0, 0.45, RelayStopKind.WindingDown), new RelayStop(1, 0.5, RelayStopKind.HandedOff)]);

    [Fact]
    public void EachConnectionRunsOnItsOwnLaneCentredInItsRow()
    {
        var route = RelayLayout.Route(Relayed, Spacing, 436);

        Assert.Equal([32d, 96d], route.Lanes);
    }

    [Fact]
    public void AHandoffJumpsFromTheEndOfOneLaneToTheNextAndTheRouteHasNoGap()
    {
        var route = RelayLayout.Route(Relayed, Spacing, 436);

        Assert.Equal([RelayPieceKind.Straight, RelayPieceKind.Jump, RelayPieceKind.Straight], route.Pieces.Select(piece => piece.Kind));
        Assert.All(route.Pieces.Zip(route.Pieces.Skip(1)), pair => Assert.Equal(pair.First.To, pair.Second.From));
        Assert.Equal(new GraphPoint(18 + 400, 96), route.Pieces[^1].To);
    }

    [Fact]
    public void TheHandoffMarkerSitsWhereTheJumpLandsOnTheNewLane()
    {
        var route = RelayLayout.Route(Relayed, Spacing, 436);

        Assert.Equal(route.Pieces[1].To, route.Markers.Single(marker => marker.Kind == RelayStopKind.HandedOff).At);
        Assert.Equal(new GraphPoint(18 + 180, 32), route.Markers.Single(marker => marker.Kind == RelayStopKind.WindingDown).At);
    }

    [Fact]
    public void AJumpNeverLandsPastTheMiddleOfAShortStretch()
    {
        var course = Relayed with { Stretches = [new RelayStretch(0, 0, 0.9, RelayStretchKind.Ran), new RelayStretch(1, 0.9, 1, RelayStretchKind.Running)] };

        var route = RelayLayout.Route(course, Spacing, 436);

        Assert.Equal(18 + 360 + 20, route.Pieces[1].To.X, 3);
    }

    [Fact]
    public void TheBatonTravelsTheRouteFromItsStartToItsEnd()
    {
        var route = RelayLayout.Route(Relayed, Spacing, 436);

        Assert.Equal(route.Pieces[0].From, route.Along(0).Match(point => point, () => default));
        Assert.Equal(route.Pieces[^1].To, route.Along(1).Match(point => point, () => default));
        Assert.Equal(route.Pieces[^1].To, route.Baton.Match(point => point, () => default));
    }

    [Fact]
    public void ACourseWithoutLanesOrRoomHasNoRoute()
    {
        Assert.Equal(RelayRoute.Empty, RelayLayout.Route(RelayCourse.Empty, Spacing, 436));
        Assert.Equal(RelayRoute.Empty, RelayLayout.Route(Relayed, Spacing, 0));
        Assert.True(RelayRoute.Empty.Along(0.5).IsNone);
    }

    [Fact]
    public void ACourseIsLiveWhileItsLastStretchRunsOrWaits()
    {
        Assert.True(Relayed.IsLive);
        Assert.False((Relayed with { Stretches = [new RelayStretch(0, 0, 1, RelayStretchKind.Ran)] }).IsLive);
        Assert.True((Relayed with { Stretches = [new RelayStretch(0, 0, 1, RelayStretchKind.Waiting)] }).IsLive);
    }
}
