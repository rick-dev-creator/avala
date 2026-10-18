namespace Avala.Components.Graphs;

public static class LinkLanes
{
    public const double Gap = 18;

    public static (double X, double Y) Shift(IReadOnlyList<GraphLink> links, GraphLink link, (double X, double Y) start, (double X, double Y) end)
    {
        var parallel = links.Where(other => Joins(other, link)).ToList();
        var length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

        if (parallel.Count < 2 || length == 0)
        {
            return (0, 0);
        }

        var lane = (parallel.IndexOf(link) - ((parallel.Count - 1) / 2d)) * Gap * (string.CompareOrdinal(link.From, link.To) <= 0 ? 1 : -1);

        return (-(end.Y - start.Y) / length * lane, (end.X - start.X) / length * lane);
    }

    private static bool Joins(GraphLink one, GraphLink other) =>
        (one.From == other.From && one.To == other.To) || (one.From == other.To && one.To == other.From);
}
