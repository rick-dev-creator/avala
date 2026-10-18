namespace Avala.Components.Graphs;

public sealed record LinkRoute(IReadOnlyList<GraphPoint> Points)
{
    public double Length => Points.Zip(Points.Skip(1), LinkRoutes.Distance).Sum();
}

public static class LinkRoutes
{
    public const double LaneGap = 22;

    private const double Clearance = 4;

    private static readonly double[] Along = [0.5, 0.35, 0.65, 0.2, 0.8];

    public static LinkRoute Route(GraphRect from, GraphRect to, IReadOnlyList<GraphRect> others, GraphPoint shift)
    {
        var start = Edge(from, to.Center);
        var end = Edge(to, from.Center);
        var straight = new LinkRoute([start.Offset(shift.X, shift.Y), end.Offset(shift.X, shift.Y)]);
        var blocking = others.Where(other => other != from && other != to).ToList();

        return Clear(straight, blocking)
            ? straight
            : Detours(from, to, blocking).Where(route => Clear(route, blocking)).OrderBy(route => route.Length).DefaultIfEmpty(straight).First();
    }

    public static GraphRect Label(LinkRoute route, GraphSize label, IReadOnlyList<GraphRect> taken)
    {
        var (a, b) = route.Points.Zip(route.Points.Skip(1)).MaxBy(segment => Distance(segment.First, segment.Second));
        var spots = Along.Select(t => GraphRect.Around(new GraphPoint(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t)), label)).ToList();

        return spots.FirstOrDefault(spot => !taken.Any(other => other.Overlaps(spot, Clearance)), spots[0]);
    }

    public static double Distance(GraphPoint a, GraphPoint b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    private static IEnumerable<LinkRoute> Detours(GraphRect from, GraphRect to, List<GraphRect> blocking)
    {
        var (top, bottom) = (Math.Min(from.Center.Y, to.Center.Y), Math.Max(from.Center.Y, to.Center.Y));
        var (left, right) = (Math.Min(from.Center.X, to.Center.X), Math.Max(from.Center.X, to.Center.X));
        var column = blocking.Where(other => other.Y < bottom && other.Bottom > top).Append(from).Append(to).ToList();
        var row = blocking.Where(other => other.X < right && other.Right > left).Append(from).Append(to).ToList();
        var east = column.Max(rect => rect.Right) + LaneGap;
        var west = column.Min(rect => rect.X) - LaneGap;
        var north = row.Min(rect => rect.Y) - LaneGap;
        var south = row.Max(rect => rect.Bottom) + LaneGap;

        yield return new LinkRoute([new(from.Right, from.Center.Y), new(east, from.Center.Y), new(east, to.Center.Y), new(to.Right, to.Center.Y)]);
        yield return new LinkRoute([new(from.X, from.Center.Y), new(west, from.Center.Y), new(west, to.Center.Y), new(to.X, to.Center.Y)]);
        yield return new LinkRoute([new(from.Center.X, from.Y), new(from.Center.X, north), new(to.Center.X, north), new(to.Center.X, to.Y)]);
        yield return new LinkRoute([new(from.Center.X, from.Bottom), new(from.Center.X, south), new(to.Center.X, south), new(to.Center.X, to.Bottom)]);
    }

    private static bool Clear(LinkRoute route, List<GraphRect> blocking) =>
        !route.Points.Zip(route.Points.Skip(1)).Any(segment => blocking.Any(rect => Cuts(segment.First, segment.Second, rect)));

    private static GraphPoint Edge(GraphRect rect, GraphPoint toward)
    {
        var center = rect.Center;
        var (dx, dy) = (toward.X - center.X, toward.Y - center.Y);

        if (dx == 0 && dy == 0)
        {
            return center;
        }

        var scale = Math.Min(Math.Abs(dx) < 0.001 ? double.MaxValue : rect.Width / 2 / Math.Abs(dx), Math.Abs(dy) < 0.001 ? double.MaxValue : rect.Height / 2 / Math.Abs(dy));

        return new GraphPoint(center.X + (dx * scale), center.Y + (dy * scale));
    }

    private static bool Cuts(GraphPoint from, GraphPoint to, GraphRect rect)
    {
        var box = new GraphRect(rect.X - Clearance, rect.Y - Clearance, rect.Width + (2 * Clearance), rect.Height + (2 * Clearance));
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
