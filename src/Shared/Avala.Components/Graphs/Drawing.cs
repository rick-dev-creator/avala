namespace Avala.Components.Graphs;

internal sealed class Drawing(List<GraphRect> rects)
{
    private const int Samples = 16;

    private readonly List<GraphRect> rects = rects;

    private readonly List<(GraphCurve Curve, GraphRect Box)> edges = [];

    public void Add(GraphRect rect, GraphCurve edge)
    {
        rects.Add(rect);
        edges.Add((edge, Box(edge)));
    }

    public (int Rects, int Edges) Mark() => (rects.Count, edges.Count);

    public void Rollback((int Rects, int Edges) mark)
    {
        rects.RemoveRange(mark.Rects, rects.Count - mark.Rects);
        edges.RemoveRange(mark.Edges, edges.Count - mark.Edges);
    }

    public bool Overlaps(GraphRect rect, double margin) => rects.Any(other => other.Overlaps(rect, margin));

    public bool IsClear(GraphRect rect, GraphCurve edge, GraphRect own, double margin)
    {
        if (Overlaps(rect, margin))
        {
            return false;
        }

        var box = Box(edge);
        var clearance = margin / 3;

        return !edges.Any(other => other.Box.Overlaps(rect, clearance) && Crosses(other.Curve, rect, clearance))
            && !rects.Any(other => other != own && box.Overlaps(other, clearance) && Crosses(edge, other, clearance));
    }

    private static GraphRect Box(GraphCurve curve)
    {
        var (left, top) = (Math.Min(curve.Start.X, Math.Min(curve.Control.X, curve.End.X)), Math.Min(curve.Start.Y, Math.Min(curve.Control.Y, curve.End.Y)));
        var (right, bottom) = (Math.Max(curve.Start.X, Math.Max(curve.Control.X, curve.End.X)), Math.Max(curve.Start.Y, Math.Max(curve.Control.Y, curve.End.Y)));

        return new GraphRect(left, top, right - left, bottom - top);
    }

    private static bool Crosses(GraphCurve curve, GraphRect rect, double margin)
    {
        var box = new GraphRect(rect.X - margin, rect.Y - margin, rect.Width + (2 * margin), rect.Height + (2 * margin));

        return Enumerable.Range(0, Samples).Any(step => Cuts(curve.At(step / (double)Samples), curve.At((step + 1) / (double)Samples), box));
    }

    private static bool Cuts(GraphPoint from, GraphPoint to, GraphRect box)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var (enter, leave) = (0.0, 1.0);

        foreach (var (p, q) in new[] { (-dx, from.X - box.X), (dx, box.Right - from.X), (-dy, from.Y - box.Y), (dy, box.Bottom - from.Y) })
        {
            if (Math.Abs(p) < 1e-9)
            {
                if (q <= 0)
                {
                    return false;
                }

                continue;
            }

            var t = q / p;
            (enter, leave) = p < 0 ? (Math.Max(enter, t), leave) : (enter, Math.Min(leave, t));
        }

        return enter < leave;
    }
}
