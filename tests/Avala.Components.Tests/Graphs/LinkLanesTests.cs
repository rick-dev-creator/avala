using Avala.Components.Graphs;

namespace Avala.Components.Tests.Graphs;

public sealed class LinkLanesTests
{
    [Fact]
    public void ALoneRelationRunsOnTheStraightLine()
    {
        var runs = new GraphLink("user", "greet", "Runs it", EdgeKind.Quiet);

        Assert.Equal((0d, 0d), LinkLanes.Shift([runs], runs, (0, 0), (100, 0)));
    }

    [Fact]
    public void TwoRelationsBetweenTheSameElementsEitherWayRunOnOppositeSidesSoTheirLabelsDoNotOverlap()
    {
        var runs = new GraphLink("user", "greet", "Runs it", EdgeKind.Quiet);
        var writes = new GraphLink("greet", "user", "Writes the greeting", EdgeKind.Quiet);
        var other = new GraphLink("greet", "disk", "Reads", EdgeKind.Quiet);
        IReadOnlyList<GraphLink> links = [runs, other, writes];

        var there = LinkLanes.Shift(links, runs, (0, 0), (100, 0));
        var back = LinkLanes.Shift(links, writes, (100, 0), (0, 0));

        Assert.Equal((0d, 0d), LinkLanes.Shift(links, other, (100, 0), (100, 100)));
        Assert.Equal(0d, there.X);
        Assert.True(there.Y * back.Y < 0, $"{there} and {back} are on the same side");
    }
}
