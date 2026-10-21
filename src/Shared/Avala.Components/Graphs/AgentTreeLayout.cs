namespace Avala.Components.Graphs;

public enum AgentTreeShape
{
    Comb,
    Rail,
}

public sealed record AgentTreeSpacing(double Gap = 12, double Indent = 24, double Drop = 28);

public sealed record AgentTreeEdge(int Node, IReadOnlyList<GraphPoint> Points)
{
    public const int Shared = -1;
}

public sealed record AgentTreeGraphLayout(
    AgentTreeShape Shape,
    IReadOnlyList<GraphRect> Nodes,
    IReadOnlyList<int> Parents,
    IReadOnlyList<AgentTreeEdge> Edges,
    IReadOnlyList<GraphPoint> Joints,
    GraphSize Size)
{
    public static AgentTreeGraphLayout Empty { get; } = new(AgentTreeShape.Comb, [], [], [], [], new GraphSize(0, 0));
}

public static class AgentTreeLayout
{
    public static AgentTreeGraphLayout Arrange(IReadOnlyList<TreeNodeSpec> nodes, AgentTreeSpacing spacing, double availableWidth)
    {
        if (nodes.Count == 0)
        {
            return AgentTreeGraphLayout.Empty;
        }

        var rooted = nodes[0].Depth <= 0;
        var parents = Parents(nodes);
        var levels = Levels(parents, rooted);
        var comb = rooted ? Comb(nodes, levels, spacing) : null;

        return comb is not null && Width(comb) <= availableWidth
            ? Drawn(AgentTreeShape.Comb, comb, parents, spacing)
            : Drawn(AgentTreeShape.Rail, Rail(nodes, levels, rooted, spacing), parents, spacing);
    }

    public static int[] Parents(IReadOnlyList<TreeNodeSpec> nodes)
    {
        var depths = nodes.Select((node, index) => index == 0 && node.Depth <= 0 ? 0 : Math.Max(1, node.Depth)).ToArray();
        var parents = new int[nodes.Count];

        for (var index = 0; index < nodes.Count; index++)
        {
            parents[index] = -1;

            for (var before = index - 1; before >= 0; before--)
            {
                if (depths[before] < depths[index])
                {
                    parents[index] = before;
                    break;
                }
            }
        }

        return parents;
    }

    private static int[] Levels(int[] parents, bool rooted)
    {
        var levels = new int[parents.Length];

        for (var index = 0; index < parents.Length; index++)
        {
            levels[index] = parents[index] >= 0 ? levels[parents[index]] + 1 : rooted && index == 0 ? 0 : 1;
        }

        return levels;
    }

    private static GraphRect[] Comb(IReadOnlyList<TreeNodeSpec> nodes, int[] levels, AgentTreeSpacing spacing)
    {
        var rects = new GraphRect[nodes.Count];
        var root = nodes[0].Size;
        var top = root.Height + spacing.Drop;
        var left = 0.0;
        var columns = new List<(double Left, double Right)>();

        for (var index = 1; index < nodes.Count; index++)
        {
            if (levels[index] <= 1)
            {
                left = columns.Count == 0 ? 0 : columns[^1].Right + spacing.Gap;
                columns.Add((left, left));
            }

            var x = left + (Math.Max(0, levels[index] - 1) * spacing.Indent);
            var y = levels[index] <= 1 ? top : rects[index - 1].Bottom + spacing.Gap;
            rects[index] = new GraphRect(x, y, nodes[index].Size.Width, nodes[index].Size.Height);
            columns[^1] = (columns[^1].Left, Math.Max(columns[^1].Right, rects[index].Right));
        }

        var span = columns.Count == 0 ? 0 : columns[^1].Right;
        var width = Math.Max(span, root.Width);
        var shift = (width - span) / 2;

        for (var index = 1; index < nodes.Count; index++)
        {
            rects[index] = rects[index] with { X = rects[index].X + shift };
        }

        rects[0] = new GraphRect((width - root.Width) / 2, 0, root.Width, root.Height);

        return rects;
    }

    private static GraphRect[] Rail(IReadOnlyList<TreeNodeSpec> nodes, int[] levels, bool rooted, AgentTreeSpacing spacing)
    {
        var rects = new GraphRect[nodes.Count];

        for (var index = 0; index < nodes.Count; index++)
        {
            var y = index == 0 ? 0 : rects[index - 1].Bottom + spacing.Gap;
            rects[index] = new GraphRect((levels[index] - (rooted ? 0 : 1)) * spacing.Indent, y, nodes[index].Size.Width, nodes[index].Size.Height);
        }

        return rects;
    }

    private static double Width(GraphRect[] rects) => rects.Max(rect => rect.Right);

    private static AgentTreeGraphLayout Drawn(AgentTreeShape shape, GraphRect[] rects, int[] parents, AgentTreeSpacing spacing)
    {
        var edges = new List<AgentTreeEdge>();
        var joints = new List<GraphPoint>();

        for (var parent = 0; parent < rects.Length; parent++)
        {
            var children = Enumerable.Range(parent + 1, rects.Length - parent - 1).Where(child => parents[child] == parent).ToList();

            if (children.Count == 0)
            {
                continue;
            }

            if (shape == AgentTreeShape.Comb && parent == 0)
            {
                Fan(rects, children, spacing, edges, joints);
            }
            else
            {
                Hang(rects, parent, children, spacing, edges, joints);
            }
        }

        return new AgentTreeGraphLayout(shape, rects, parents, edges, joints, new GraphSize(Width(rects), rects.Max(rect => rect.Bottom)));
    }

    private static void Fan(GraphRect[] rects, List<int> children, AgentTreeSpacing spacing, List<AgentTreeEdge> edges, List<GraphPoint> joints)
    {
        var root = rects[0];
        var stem = new GraphPoint(root.Center.X, root.Bottom);
        var bar = root.Bottom + (spacing.Drop / 2);
        var drops = children.Select(child => rects[child].Center.X).ToList();
        var (from, to) = (Math.Min(stem.X, drops.Min()), Math.Max(stem.X, drops.Max()));

        joints.Add(stem);
        edges.Add(new AgentTreeEdge(AgentTreeEdge.Shared, [stem, new GraphPoint(stem.X, bar)]));

        if (to > from)
        {
            edges.Add(new AgentTreeEdge(AgentTreeEdge.Shared, [new GraphPoint(from, bar), new GraphPoint(to, bar)]));
        }

        edges.AddRange(children.Select(child => new AgentTreeEdge(child, [new GraphPoint(rects[child].Center.X, bar), new GraphPoint(rects[child].Center.X, rects[child].Y)])));
    }

    private static void Hang(GraphRect[] rects, int parent, List<int> children, AgentTreeSpacing spacing, List<AgentTreeEdge> edges, List<GraphPoint> joints)
    {
        var rail = rects[parent].X + (spacing.Indent / 2);
        var above = rects[parent].Bottom;

        joints.Add(new GraphPoint(rail, above));

        foreach (var child in children)
        {
            var tick = rects[child].Center.Y;
            edges.Add(new AgentTreeEdge(child, [new GraphPoint(rail, above), new GraphPoint(rail, tick), new GraphPoint(rects[child].X, tick)]));
            above = tick;
        }
    }
}
