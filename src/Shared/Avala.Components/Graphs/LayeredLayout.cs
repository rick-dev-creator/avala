namespace Avala.Components.Graphs;

public sealed record LayeredNodeSpec(GraphSize Size)
{
    public int Group { get; init; } = -1;
}

public sealed record LayeredLink(int From, int To);

public sealed record LayeredSpacing(double ColumnGap = 56, double RowGap = 72, double GroupPadding = 22, double GroupLabel = 22);

public sealed record LayeredGraphLayout(IReadOnlyList<GraphRect> Nodes, IReadOnlyList<GraphRect> Groups, GraphSize Size)
{
    public static LayeredGraphLayout Empty { get; } = new([], [], new GraphSize(0, 0));
}

public static class LayeredLayout
{
    public static LayeredGraphLayout Arrange(IReadOnlyList<LayeredNodeSpec> nodes, IReadOnlyList<LayeredLink> links, int groups, LayeredSpacing spacing)
    {
        if (nodes.Count == 0)
        {
            return LayeredGraphLayout.Empty;
        }

        var valid = links.Where(link => link.From != link.To && link.From >= 0 && link.To >= 0 && link.From < nodes.Count && link.To < nodes.Count).ToList();
        var ranks = Ranks(nodes.Count, valid);
        var rows = Rows(nodes, valid, ranks);
        var placed = Place(nodes, rows, groups, spacing);
        var boxes = Enumerable.Range(0, groups).Select(group => Box(nodes, placed, group, spacing)).ToList();
        var left = Math.Min(placed.Min(rect => rect.X), boxes.Where(box => box.Width > 0).Select(box => box.X).DefaultIfEmpty(0).Min());
        var top = Math.Min(placed.Min(rect => rect.Y), boxes.Where(box => box.Width > 0).Select(box => box.Y).DefaultIfEmpty(0).Min());
        var nodesShifted = placed.Select(rect => rect with { X = rect.X - left, Y = rect.Y - top }).ToList();
        var groupsShifted = boxes.Select(box => box.Width > 0 ? box with { X = box.X - left, Y = box.Y - top } : box).ToList();
        var width = nodesShifted.Concat(groupsShifted).Max(rect => rect.Right);
        var height = nodesShifted.Concat(groupsShifted).Max(rect => rect.Bottom);

        return new LayeredGraphLayout(nodesShifted, groupsShifted, new GraphSize(width, height));
    }

    public static int[] Ranks(int count, IReadOnlyList<LayeredLink> links)
    {
        var ranks = new int[count];
        var incoming = new int[count];

        foreach (var link in links)
        {
            incoming[link.To]++;
        }

        var ready = new Queue<int>(Enumerable.Range(0, count).Where(node => incoming[node] == 0));
        var done = new bool[count];

        while (ready.Count > 0 || done.Contains(false))
        {
            var node = ready.Count > 0 ? ready.Dequeue() : Array.IndexOf(done, false);

            if (done[node])
            {
                continue;
            }

            done[node] = true;

            foreach (var next in links.Where(link => link.From == node && !done[link.To]).Select(link => link.To))
            {
                ranks[next] = Math.Max(ranks[next], ranks[node] + 1);

                if (--incoming[next] == 0)
                {
                    ready.Enqueue(next);
                }
            }
        }

        return ranks;
    }

    private static List<List<int>> Rows(IReadOnlyList<LayeredNodeSpec> nodes, IReadOnlyList<LayeredLink> links, int[] ranks)
    {
        var rows = Enumerable.Range(0, ranks.Max() + 1).Select(rank => Enumerable.Range(0, nodes.Count).Where(node => ranks[node] == rank).ToList()).ToList();
        var position = new double[nodes.Count];

        foreach (var row in rows)
        {
            var centers = row.ToDictionary(node => node, node =>
            {
                var above = links.Where(link => link.To == node && ranks[link.From] < ranks[node]).Select(link => position[link.From]).ToList();

                return above.Count > 0 ? above.Average() : row.IndexOf(node);
            });
            var ordered = row.OrderBy(node => centers[node]).ThenBy(node => node).ToList();
            var grouped = ordered.GroupBy(node => nodes[node].Group < 0 ? -1 - node : nodes[node].Group).SelectMany(group => group).ToList();

            for (var index = 0; index < grouped.Count; index++)
            {
                position[grouped[index]] = index;
            }

            row.Clear();
            row.AddRange(grouped);
        }

        return rows;
    }

    private static GraphRect[] Place(IReadOnlyList<LayeredNodeSpec> nodes, List<List<int>> rows, int groups, LayeredSpacing spacing)
    {
        var placed = new GraphRect[nodes.Count];
        var regions = rows.Select(row => Regions(nodes, row, groups)).ToList();
        var count = groups + 2;
        var widths = Enumerable.Range(0, count).Select(region => regions.Max(row => Width(nodes, row[region], spacing))).ToList();
        var lefts = Lefts(widths, groups, spacing);

        var y = 0.0;

        for (var index = 0; index < rows.Count; index++)
        {
            var height = rows[index].Count == 0 ? 0 : rows[index].Max(node => nodes[node].Size.Height);

            for (var region = 0; region < count; region++)
            {
                var members = regions[index][region];
                var left = lefts[region] + ((widths[region] - Width(nodes, members, spacing)) / 2);

                foreach (var node in members)
                {
                    var size = nodes[node].Size;
                    placed[node] = new GraphRect(left, y + ((height - size.Height) / 2), size.Width, size.Height);
                    left += size.Width + spacing.ColumnGap;
                }
            }

            y += height + spacing.RowGap + (groups > 0 ? spacing.GroupLabel : 0);
        }

        return placed;
    }

    private static double[] Lefts(List<double> widths, int groups, LayeredSpacing spacing)
    {
        var lefts = new double[widths.Count];
        var x = 0.0;

        for (var region = 0; region < widths.Count; region++)
        {
            var framed = region > 0 && region <= groups && widths[region] > 0 ? spacing.GroupPadding : 0;
            lefts[region] = x + framed;
            x += widths[region] > 0 ? widths[region] + (2 * framed) + spacing.ColumnGap : 0;
        }

        return lefts;
    }

    private static List<int>[] Regions(IReadOnlyList<LayeredNodeSpec> nodes, List<int> row, int groups)
    {
        var regions = Enumerable.Range(0, groups + 2).Select(_ => new List<int>()).ToArray();
        var first = row.FindIndex(node => nodes[node].Group >= 0);

        for (var index = 0; index < row.Count; index++)
        {
            var group = nodes[row[index]].Group;
            regions[group >= 0 && group < groups ? group + 1 : first >= 0 && index > first ? groups + 1 : 0].Add(row[index]);
        }

        return regions;
    }

    private static double Width(IReadOnlyList<LayeredNodeSpec> nodes, List<int> members, LayeredSpacing spacing) =>
        members.Count == 0 ? 0 : members.Sum(node => nodes[node].Size.Width) + ((members.Count - 1) * spacing.ColumnGap);

    private static GraphRect Box(IReadOnlyList<LayeredNodeSpec> nodes, GraphRect[] placed, int group, LayeredSpacing spacing)
    {
        var members = Enumerable.Range(0, nodes.Count).Where(node => nodes[node].Group == group).Select(node => placed[node]).ToList();

        if (members.Count == 0)
        {
            return new GraphRect(0, 0, 0, 0);
        }

        var left = members.Min(rect => rect.X) - spacing.GroupPadding;
        var top = members.Min(rect => rect.Y) - spacing.GroupPadding - spacing.GroupLabel;

        return new GraphRect(left, top, members.Max(rect => rect.Right) + spacing.GroupPadding - left, members.Max(rect => rect.Bottom) + spacing.GroupPadding - top);
    }
}

public sealed record GraphLink(string From, string To, string Label, EdgeKind Kind)
{
    public string Badge { get; init; } = string.Empty;
}

public sealed record GraphFrame(string Key, string Label);
