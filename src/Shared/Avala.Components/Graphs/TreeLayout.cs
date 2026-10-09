namespace Avala.Components.Graphs;

public sealed record TreeNodeSpec(int Depth, GraphSize Size);

public sealed record TreeSpacing(double Lead = 240, double ColumnGap = 64, double RowGap = 28);

public sealed record TreePlacement(GraphRect Bounds, int Parent);

public sealed record TreeGraphLayout(IReadOnlyList<TreePlacement> Nodes, GraphPoint Root, GraphSize Size)
{
    public static TreeGraphLayout Empty { get; } = new([], new GraphPoint(0, 0), new GraphSize(0, 0));
}

public static class TreeLayout
{
    public static TreeGraphLayout Arrange(IReadOnlyList<TreeNodeSpec> nodes, TreeSpacing spacing)
    {
        if (nodes.Count == 0)
        {
            return TreeGraphLayout.Empty;
        }

        var parents = Parents(nodes);
        var depths = nodes.Select(node => Math.Max(1, node.Depth)).ToArray();
        var columns = Enumerable.Range(1, depths.Max()).ToDictionary(depth => depth, depth => nodes.Where((_, index) => depths[index] == depth).Max(node => node.Size.Width));
        var row = nodes.Max(node => node.Size.Height) + spacing.RowGap;
        var centers = new double[nodes.Count];
        var next = 0.0;

        for (var index = 0; index < nodes.Count; index++)
        {
            if (!parents.Contains(index))
            {
                centers[index] = next + ((row - spacing.RowGap) / 2);
                next += row;
            }
        }

        Recenter(centers, parents);
        var top = Enumerable.Range(0, nodes.Count).Where(index => parents[index] < 0).Select(index => centers[index]).ToList();
        var root = (top[0] + top[^1]) / 2;
        var height = next - spacing.RowGap;
        var half = Math.Max(root, height - root);
        var shift = half - root;
        var placements = nodes.Select((node, index) => new TreePlacement(
            new GraphRect(X(depths[index], columns, spacing), centers[index] + shift - (node.Size.Height / 2), node.Size.Width, node.Size.Height),
            parents[index])).ToList();
        var width = placements.Max(placement => placement.Bounds.Right);

        return new TreeGraphLayout(placements, new GraphPoint(0, half), new GraphSize(width, Math.Max(half * 2, row - spacing.RowGap)));
    }

    public static int[] Parents(IReadOnlyList<TreeNodeSpec> nodes)
    {
        var parents = new int[nodes.Count];

        for (var index = 0; index < nodes.Count; index++)
        {
            var depth = Math.Max(1, nodes[index].Depth);
            parents[index] = -1;

            for (var before = index - 1; before >= 0 && depth > 1; before--)
            {
                if (Math.Max(1, nodes[before].Depth) == depth - 1)
                {
                    parents[index] = before;
                    break;
                }
            }
        }

        return parents;
    }

    private static void Recenter(double[] centers, int[] parents)
    {
        for (var index = centers.Length - 1; index >= 0; index--)
        {
            var children = Enumerable.Range(0, centers.Length).Where(child => parents[child] == index).ToList();

            if (children.Count > 0)
            {
                centers[index] = (centers[children[0]] + centers[children[^1]]) / 2;
            }
        }
    }

    private static double X(int depth, Dictionary<int, double> columns, TreeSpacing spacing) =>
        spacing.Lead + Enumerable.Range(1, depth - 1).Sum(column => columns[column] + spacing.ColumnGap);
}
