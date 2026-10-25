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

        Assert.Equal((AgentTreeShape.Tree, new GraphRect(0, 0, 200, 80), new GraphSize(200, 80)), (layout.Shape, layout.Nodes[0], layout.Size));
        Assert.Empty(layout.Edges);
        Assert.Empty(layout.Joints);
    }

    [Fact]
    public void FirstLevelSubAgentsStandSideBySideTopAlignedUnderTheOrchestratorCentredOverThem()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 200, 80), Card(1, 100, 60), Card(1, 120, 90), Card(1, 100, 50)], Spacing, double.PositiveInfinity);

        Assert.Equal(AgentTreeShape.Tree, layout.Shape);
        Assert.Equal([0d, 110, 240], layout.Nodes.Skip(1).Select(node => node.X));
        Assert.All(layout.Nodes.Skip(1), node => Assert.Equal(110, node.Y));
        Assert.Equal(new GraphRect(70, 0, 200, 80), layout.Nodes[0]);
        Assert.Equal(new GraphSize(340, 200), layout.Size);
    }

    [Fact]
    public void EveryCardStandsCentredOverItsChildrenWhichStandSideBySideOneRowBelowItAtEveryDepth()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(3, 100, 40), Card(2, 100, 40), Card(1, 100, 60)], Spacing, double.PositiveInfinity);

        Assert.Equal(AgentTreeShape.Tree, layout.Shape);
        Assert.Equal([(110d, 0d), (55, 70), (0, 160), (0, 230), (110, 160), (220, 70)], layout.Nodes.Select(node => (node.X, node.Y)));
        Assert.Equal(["-1: 105,130 105,145", "-1: 50,145 160,145", "2: 50,145 50,160", "4: 160,145 160,160", "-1: 50,200 50,215", "3: 50,215 50,230"], layout.Edges.Where(edge => edge.Node is 2 or 3 or 4 || (edge.Node == -1 && edge.Points[0].Y > 100)).Select(Path));
    }

    [Fact]
    public void ATreeTooWideToFanEveryLevelStacksALastRowOfLeavesUnderTheirParentAndStillFansEveryBranchAboveThem()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 60), Card(3, 100, 40), Card(3, 100, 40), Card(3, 100, 40), Card(2, 100, 60), Card(1, 100, 60)], Spacing, 400);

        Assert.Equal(AgentTreeShape.Stacked, layout.Shape);
        Assert.Equal([(120d, 0d), (65, 70), (0, 160), (20, 230), (20, 280), (20, 330), (130, 160), (240, 70)], layout.Nodes.Select(node => (node.X, node.Y)));
        Assert.Equal(["3: 10,220 10,250 20,250", "4: 10,250 10,300 20,300", "5: 10,300 10,350 20,350"], layout.Edges.Where(edge => edge.Node is 3 or 4 or 5).Select(Path));
    }

    [Fact]
    public void ATreeTooWideForItsRoomKeepsItsFirstLevelSideBySideAndHangsDeeperCardsInTheirColumn()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(3, 100, 40), Card(2, 100, 40), Card(1, 100, 60)], Spacing, 300);

        Assert.Equal(AgentTreeShape.Comb, layout.Shape);
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
        Assert.Equal((layout.Nodes[1].X, layout.Nodes[1].Bottom + 30), (layout.Nodes[2].X, layout.Nodes[2].Y));
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
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(2, 100, 40)], Spacing, 150);

        Assert.Equal(new GraphPoint(10, 130), layout.Joints[^1]);
        Assert.Equal(["2: 10,130 10,160 20,160", "3: 10,160 10,210 20,210"], layout.Edges.Where(edge => edge.Node >= 2).Select(Path));
    }

    [Fact]
    public void ATreeNarrowerThanItsRoomStandsCentredInItWithItsEdgesAndJoints()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 200, 80), Card(1, 100, 60), Card(1, 120, 90), Card(1, 100, 50)], Spacing, double.PositiveInfinity);

        var centred = AgentTreeLayout.Centered(layout, 540);

        Assert.Equal([100d, 210, 340], centred.Nodes.Skip(1).Select(node => node.X));
        Assert.Equal((170d, new GraphSize(540, 200)), (centred.Nodes[0].X, centred.Size));
        Assert.Equal(layout.Edges.SelectMany(edge => edge.Points).Select(point => point.X + 100), centred.Edges.SelectMany(edge => edge.Points).Select(point => point.X));
        Assert.Equal(layout.Joints.Select(joint => joint.X + 100), centred.Joints.Select(joint => joint.X));
    }

    [Fact]
    public void ARailedTreeKeepsToTheLeftOfItsRoom()
    {
        var layout = AgentTreeLayout.Arrange([Card(0, 100, 40), Card(1, 100, 60), Card(2, 100, 40), Card(1, 100, 60)], Spacing, 209);

        Assert.Same(layout, AgentTreeLayout.Centered(layout, 600));
    }

    [Fact]
    public void AFailedSubAgentDrawsAFailureEdge() =>
        Assert.Equal(EdgeKind.Failure, EdgeKinds.Of(StatusKind.Failed));

    private static string Path(AgentTreeEdge edge) =>
        $"{edge.Node}: {string.Join(' ', edge.Points.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X},{point.Y}")))}";

    private static TreeNodeSpec Card(int depth, double width, double height) => new(depth, new GraphSize(width, height));
}
