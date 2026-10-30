namespace Avala.Components.Graphs;

public enum AgentTreeShape
{
    Tree,
    Stacked,
    Comb,
    Rail,
}

public sealed record TreeNodeSpec(int Depth, GraphSize Size);

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

    public AgentTreeSpacing Spacing { get; init; } = new();
}

public sealed record AgentTreeRoutes(IReadOnlyList<AgentTreeEdge> Edges, IReadOnlyList<GraphPoint> Joints)
{
    public static AgentTreeRoutes Empty { get; } = new([], []);

    public IReadOnlyList<GraphRect> Leaving { get; init; } = [];
}

public static class AgentTreeLayout
{
    public static AgentTreeGraphLayout Arrange(IReadOnlyList<TreeNodeSpec> nodes, AgentTreeSpacing spacing, double availableWidth) =>
        Arrange(nodes, spacing, availableWidth, railsOnly: false);

    public static AgentTreeGraphLayout Arrange(IReadOnlyList<TreeNodeSpec> nodes, AgentTreeSpacing spacing, double availableWidth, bool railsOnly)
    {
        if (nodes.Count == 0)
        {
            return AgentTreeGraphLayout.Empty;
        }

        var rooted = nodes[0].Depth <= 0;
        var parents = Parents(nodes);
        var levels = Levels(parents, rooted);
        var fitting = rooted && !railsOnly
            ? Candidates(nodes, parents, levels, spacing).FirstOrDefault(candidate => candidate.Rects is not null && Width(candidate.Rects) <= availableWidth)
            : default;

        return fitting.Rects is { } rects
            ? Drawn(fitting.Shape, rects, parents, spacing)
            : Drawn(AgentTreeShape.Rail, Rail(nodes, levels, rooted, spacing), parents, spacing);
    }

    public static AgentTreeGraphLayout Centered(AgentTreeGraphLayout layout, double width)
    {
        var shift = layout.Shape == AgentTreeShape.Rail || !double.IsFinite(width) ? 0 : (width - layout.Size.Width) / 2;

        if (shift <= 0)
        {
            return layout;
        }

        return layout with
        {
            Nodes = [.. layout.Nodes.Select(node => node with { X = node.X + shift })],
            Edges = [.. layout.Edges.Select(edge => edge with { Points = [.. edge.Points.Select(point => point with { X = point.X + shift })] })],
            Joints = [.. layout.Joints.Select(joint => joint with { X = joint.X + shift })],
            Size = layout.Size with { Width = width },
        };
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

    private static IEnumerable<(AgentTreeShape Shape, GraphRect[]? Rects)> Candidates(IReadOnlyList<TreeNodeSpec> nodes, int[] parents, int[] levels, AgentTreeSpacing spacing)
    {
        yield return (AgentTreeShape.Tree, Tree(nodes, parents, levels, spacing));
        yield return (AgentTreeShape.Stacked, Stacked(nodes, parents, spacing));
        yield return (AgentTreeShape.Comb, Comb(nodes, levels, spacing));
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

    private static GraphRect[] Tree(IReadOnlyList<TreeNodeSpec> nodes, int[] parents, int[] levels, AgentTreeSpacing spacing)
    {
        var children = Enumerable.Range(0, nodes.Count).Select(parent => Enumerable.Range(parent + 1, nodes.Count - parent - 1).Where(child => parents[child] == parent).ToList()).ToList();
        var spans = new double[nodes.Count];

        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            var below = children[index].Sum(child => spans[child]) + (Math.Max(0, children[index].Count - 1) * spacing.Gap);
            spans[index] = Math.Max(nodes[index].Size.Width, below);
        }

        var rows = new double[levels.Max() + 2];

        for (var index = 0; index < nodes.Count; index++)
        {
            rows[levels[index] + 1] = Math.Max(rows[levels[index] + 1], nodes[index].Size.Height);
        }

        var tops = new double[rows.Length];

        for (var level = 1; level < rows.Length; level++)
        {
            tops[level] = tops[level - 1] + (level > 1 ? rows[level - 1] + spacing.Drop : 0);
        }

        var rects = new GraphRect[nodes.Count];
        Place(0, 0);

        return rects;

        void Place(int node, double left)
        {
            rects[node] = new GraphRect(left + ((spans[node] - nodes[node].Size.Width) / 2), tops[levels[node] + 1], nodes[node].Size.Width, nodes[node].Size.Height);
            var below = children[node].Sum(child => spans[child]) + (Math.Max(0, children[node].Count - 1) * spacing.Gap);
            var next = left + ((spans[node] - below) / 2);

            foreach (var child in children[node])
            {
                Place(child, next);
                next += spans[child] + spacing.Gap;
            }
        }
    }

    private static GraphRect[]? Stacked(IReadOnlyList<TreeNodeSpec> nodes, int[] parents, AgentTreeSpacing spacing)
    {
        var children = Enumerable.Range(0, nodes.Count).Select(parent => Enumerable.Range(parent + 1, nodes.Count - parent - 1).Where(child => parents[child] == parent).ToList()).ToList();
        var stacks = children.Select(below => below.Count > 0 && below.All(child => children[child].Count == 0)).ToArray();

        if (stacks[0])
        {
            return null;
        }

        var spans = new double[nodes.Count];

        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            spans[index] = stacks[index]
                ? Math.Max(nodes[index].Size.Width, spacing.Indent + children[index].Max(child => nodes[child].Size.Width))
                : Math.Max(nodes[index].Size.Width, children[index].Sum(child => spans[child]) + (Math.Max(0, children[index].Count - 1) * spacing.Gap));
        }

        var rects = new GraphRect[nodes.Count];
        Place(0, 0, 0);

        return rects;

        void Place(int node, double left, double top)
        {
            var size = nodes[node].Size;

            if (stacks[node])
            {
                rects[node] = new GraphRect(left, top, size.Width, size.Height);
                var next = rects[node].Bottom + spacing.Gap;

                foreach (var child in children[node])
                {
                    rects[child] = new GraphRect(left + spacing.Indent, next, nodes[child].Size.Width, nodes[child].Size.Height);
                    next = rects[child].Bottom + spacing.Gap;
                }

                return;
            }

            rects[node] = new GraphRect(left + ((spans[node] - size.Width) / 2), top, size.Width, size.Height);
            var below = children[node].Sum(child => spans[child]) + (Math.Max(0, children[node].Count - 1) * spacing.Gap);
            var from = left + ((spans[node] - below) / 2);

            foreach (var child in children[node])
            {
                Place(child, from, rects[node].Bottom + spacing.Drop);
                from += spans[child] + spacing.Gap;
            }
        }
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
        var routes = Connect(shape, rects, parents, spacing, [.. rects.Select(_ => 1d)]);

        return new AgentTreeGraphLayout(shape, rects, parents, routes.Edges, routes.Joints, new GraphSize(Width(rects), rects.Max(rect => rect.Bottom))) { Spacing = spacing };
    }

    public static AgentTreeRoutes Connect(AgentTreeShape shape, IReadOnlyList<GraphRect> rects, IReadOnlyList<int> parents, AgentTreeSpacing spacing, IReadOnlyList<double> grown)
    {
        var edges = new List<AgentTreeEdge>();
        var joints = new List<GraphPoint>();

        for (var parent = 0; parent < rects.Count; parent++)
        {
            var children = Enumerable.Range(parent + 1, rects.Count - parent - 1).Where(child => parents[child] == parent).ToList();

            if (children.Count == 0)
            {
                continue;
            }

            if (shape == AgentTreeShape.Tree || (shape == AgentTreeShape.Comb && parent == 0) || (shape == AgentTreeShape.Stacked && children.Any(child => Enumerable.Range(child + 1, rects.Count - child - 1).Any(grand => parents[grand] == child))))
            {
                Fan(rects, parent, children, spacing, grown, edges, joints);
            }
            else
            {
                Hang(rects, parent, children, spacing, grown, edges, joints);
            }
        }

        return new AgentTreeRoutes(edges, joints);
    }

    public static IReadOnlyList<GraphPoint> Partial(IReadOnlyList<GraphPoint> points, double fraction)
    {
        if (fraction >= 1)
        {
            return points;
        }

        var left = Math.Max(0, fraction) * Length(points);
        var drawn = new List<GraphPoint> { points[0] };

        for (var index = 1; index < points.Count && left > 0; index++)
        {
            var (from, to) = (points[index - 1], points[index]);
            var span = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
            var share = span <= 0 ? 1 : Math.Min(1, left / span);
            drawn.Add(new GraphPoint(from.X + ((to.X - from.X) * share), from.Y + ((to.Y - from.Y) * share)));
            left -= span;
        }

        return drawn.Count > 1 ? drawn : [];
    }

    private static double Length(IReadOnlyList<GraphPoint> points)
    {
        var length = 0.0;

        for (var index = 1; index < points.Count; index++)
        {
            length += Math.Abs(points[index].X - points[index - 1].X) + Math.Abs(points[index].Y - points[index - 1].Y);
        }

        return length;
    }

    private static void Fan(IReadOnlyList<GraphRect> rects, int parent, List<int> children, AgentTreeSpacing spacing, IReadOnlyList<double> grown, List<AgentTreeEdge> edges, List<GraphPoint> joints)
    {
        var root = rects[parent];
        var stem = new GraphPoint(root.Center.X, root.Bottom);
        var bar = children.Min(child => rects[child].Y) - (spacing.Drop / 2);
        var rise = bar - stem.Y;
        var (from, to, down) = (stem.X, stem.X, 0.0);
        var drops = new List<AgentTreeEdge>();

        foreach (var child in children)
        {
            var x = rects[child].Center.X;
            var across = Math.Abs(x - stem.X);
            var fall = rects[child].Y - bar;
            var reach = grown[child] >= 1 ? double.PositiveInfinity : Math.Max(0, grown[child]) * (rise + across + fall);
            var along = Math.Clamp(reach - rise, 0, across);
            var dropped = Math.Min(fall, reach - rise - across);
            down = Math.Max(down, Math.Min(rise, reach));
            from = Math.Min(from, x < stem.X ? stem.X - along : from);
            to = Math.Max(to, x > stem.X ? stem.X + along : to);

            if (dropped > 0)
            {
                drops.Add(new AgentTreeEdge(child, [new GraphPoint(x, bar), new GraphPoint(x, bar + dropped)]));
            }
        }

        joints.Add(stem);

        if (down > 0)
        {
            edges.Add(new AgentTreeEdge(AgentTreeEdge.Shared, [stem, new GraphPoint(stem.X, stem.Y + down)]));
        }

        if (to > from)
        {
            edges.Add(new AgentTreeEdge(AgentTreeEdge.Shared, [new GraphPoint(from, bar), new GraphPoint(to, bar)]));
        }

        edges.AddRange(drops);
    }

    private static void Hang(IReadOnlyList<GraphRect> rects, int parent, List<int> children, AgentTreeSpacing spacing, IReadOnlyList<double> grown, List<AgentTreeEdge> edges, List<GraphPoint> joints)
    {
        var rail = rects[parent].X + (spacing.Indent / 2);
        var above = rects[parent].Bottom;

        joints.Add(new GraphPoint(rail, above));

        foreach (var child in children)
        {
            var tick = rects[child].Center.Y;
            var drawn = Partial([new GraphPoint(rail, above), new GraphPoint(rail, tick), new GraphPoint(rects[child].X, tick)], grown[child]);

            if (drawn.Count > 1)
            {
                edges.Add(new AgentTreeEdge(child, drawn));
            }

            above = tick;
        }
    }
}
