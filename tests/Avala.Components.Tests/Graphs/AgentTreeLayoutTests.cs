using System.Globalization;
using Avala.Components.Graphs;
using Avala.Components.Status;

namespace Avala.Components.Tests.Graphs;

public sealed class AgentTreeLayoutTests
{
    private static readonly AgentTreeSpacing Spacing = new(Gap: 10, Indent: 20, Drop: 30);

    [Fact]
    public void AnEmptyTreeTakesNoRoom() =>
        Assert.Same(AgentTreeGraphLayout.Empty, AgentTreeLayout.Arrange([], Spacing, double.PositiveInfinity));

    [Fact]
    public void ALoneOrchestratorSitsAtTheOriginWithNoEdges()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 200, 80)], Spacing, double.PositiveInfinity);

        Assert.Equal((AgentTreeShape.Comb, new GraphRect(0, 0, 200, 80), new GraphSize(200, 80)), (layout.Shape, layout.Nodes[0], layout.Size));
        Assert.Empty(layout.Edges);
        Assert.Empty(layout.Joints);
    }

    [Fact]
    public void FirstLevelSubAgentsStandSideBySideTopAlignedUnderTheOrchestratorCentredOverThem()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 200, 80), Card(1, 100, 60), Card(1, 120, 90), Card(1, 100, 50)], Spacing, double.PositiveInfinity);

        Assert.Equal(AgentTreeShape.Comb, layout.Shape);
        Assert.Equal([0d, 110, 240], layout.Nodes.Skip(1).Select(node => node.X));
        Assert.All(layout.Nodes.Skip(1), node => Assert.Equal(110, node.Y));
        Assert.Equal(new GraphRect(70, 0, 200, 80), layout.Nodes[0]);
        Assert.Equal(new GraphSize(340, 200), layout.Size);
    }

    [Fact]
    public void DeeperSubAgentsHangInTheirColumnIndentedPerLevelAndWidenIt()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(3, 100, 40), Card(2, 100, 40), Card(1, 100, 60)], Spacing, double.PositiveInfinity);

        Assert.Equal([(0d, 70d), (20, 140), (40, 190), (20, 240), (150, 70)], layout.Nodes.Skip(1).Select(node => (node.X, node.Y)));
    }

    [Fact]
    public void ATreeWiderThanItsRoomHangsEveryLevelOnRailsUnderTheOrchestrator()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(1, 100, 60)], Spacing, 209);

        Assert.Equal(AgentTreeShape.Rail, layout.Shape);
        Assert.Equal([(0d, 0d), (20, 50), (40, 120), (20, 170)], layout.Nodes.Select(node => (node.X, node.Y)));
        Assert.Equal(new GraphSize(140, 230), layout.Size);
    }

    [Fact]
    public void ACardDeeperThanTheNextLevelHangsFromTheNearestShallowerCardOneLevelIn()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(3, 100, 40)], Spacing, double.PositiveInfinity);

        Assert.Equal([-1, 0, 1], layout.Parents);
        Assert.Equal(20, layout.Nodes[2].X);
    }

    [Fact]
    public void ATreeWithoutAnOrchestratorHangsItsTopCardsOnTheLeftWithNoEdgeIntoThem()
    {
        var layout = AgentTreeLayout.Arrange([Card(1, 100, 40), Card(2, 100, 40), Card(1, 100, 40)], Spacing, double.PositiveInfinity);

        Assert.Equal(AgentTreeShape.Rail, layout.Shape);
        Assert.Equal([(0d, 0d), (20, 50), (0, 100)], layout.Nodes.Select(node => (node.X, node.Y)));
        Assert.Equal([1], layout.Edges.Select(edge => edge.Node));
    }

    [Fact]
    public void TheCombDropsAQuietStemAndBarFromTheOrchestratorAndOneLineIntoEachSubAgentsTop()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 200, 80), Card(1, 100, 60), Card(1, 100, 60)], Spacing, double.PositiveInfinity);

        Assert.Equal([new GraphPoint(105, 80)], layout.Joints);
        Assert.Equal(["-1: 105,80 105,95", "-1: 50,95 160,95", "1: 50,95 50,110", "2: 160,95 160,110"], layout.Edges.Select(Path));
    }

    [Fact]
    public void ARailRunsDownFromUnderItsParentAndTicksIntoEachChildsMiddleOneStretchPerChild()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(2, 100, 40)], Spacing, double.PositiveInfinity);

        Assert.Equal(new GraphPoint(10, 130), layout.Joints[^1]);
        Assert.Equal(["2: 10,130 10,160 20,160", "3: 10,160 10,210 20,210"], layout.Edges.Where(edge => edge.Node >= 2).Select(Path));
    }

    [Fact]
    public void AFailedSubAgentDrawsAFailureEdge() =>
        Assert.Equal(EdgeKind.Failure, EdgeKinds.Of(StatusKind.Failed));

    private static string Path(AgentTreeEdge edge) =>
        $"{edge.Node}: {string.Join(' ', edge.Points.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X},{point.Y}")))}";

    private static TreeNodeSpec Card(int depth, double width, double height) => new(depth, new GraphSize(width, height));
}
