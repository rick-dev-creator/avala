namespace Avala.Components.Graphs;

public sealed record LinkRoute(IReadOnlyList<GraphPoint> Points)
{
    public double Length => Points.Zip(Points.Skip(1), LinkRoutes.Distance).Sum();
}

public static class LinkRoutes
{
    public const double LaneGap = 34;

    private const double Clearance = 4;

    private const double FarAway = 40;

    private static readonly double[] Along = [0.5, 0.35, 0.65, 0.2, 0.8, 0.1, 0.9];

    public static LinkRoute Route(GraphRect from, GraphRect to, IReadOnlyList<GraphRect> others, GraphPoint shift)
    {
        var (origin, target) = (Inside(from, from.Center.Offset(shift.X, shift.Y)), Inside(to, to.Center.Offset(shift.X, shift.Y)));
        var straight = new LinkRoute([Exit(from, origin, target), Exit(to, target, origin)]);
        var blocking = others.Where(other => other != from && other != to).ToList();
        var center = new LinkRoute([Exit(from, from.Center, to.Center), Exit(to, to.Center, from.Center)]);

        if (Clear(center, blocking, Clearance + (1.5 * LinkLanes.Gap)))
        {
            return straight;
        }

        var lane = Math.Sqrt((shift.X * shift.X) + (shift.Y * shift.Y)) * (Math.Abs(shift.X) >= Math.Abs(shift.Y) ? Math.Sign(shift.X) : Math.Sign(shift.Y));
        var side = Detours(from, to, blocking, 0)
            .Select((route, index) => (Route: route, Index: index))
            .Where(found => Clear(found.Route, blocking, Clearance) && Apart(found.Route, from, to))
            .OrderBy(found => found.Route.Length)
            .Select(found => found.Index)
            .DefaultIfEmpty(-1)
            .First();

        return side < 0 ? straight : Detours(from, to, blocking, lane).ElementAt(side);
    }

    public static GraphRect Label(LinkRoute route, GraphSize label, IReadOnlyList<GraphRect> taken, int reach = 2)
    {
        var (a, b) = route.Points.Zip(route.Points.Skip(1)).MaxBy(segment => Distance(segment.First, segment.Second));
        var length = Distance(a, b);
        var (nx, ny) = length > 0 ? (-(b.Y - a.Y) / length, (b.X - a.X) / length) : (1d, 0d);
        var beside = (Math.Abs(nx) * label.Width / 2) + (Math.Abs(ny) * label.Height / 2) + Clearance + 2;
        var spots = Enumerable.Range(0, (2 * reach) + 1).Select(step => step % 2 == 0 ? -(step / 2) : (step + 1) / 2)
            .SelectMany(side => Along.Select(t => (Side: side, Spot: GraphRect.Around(new GraphPoint(a.X + ((b.X - a.X) * t) + (nx * side * beside), a.Y + ((b.Y - a.Y) * t) + (ny * side * beside)), label))))
            .ToList();

        return spots.MinBy(found => taken.Sum(other => Covered(found.Spot, other)) + (FarAway * Math.Abs(found.Side))).Spot;
    }

    public static double Distance(GraphPoint a, GraphPoint b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

    private static double Covered(GraphRect one, GraphRect other)
    {
        var width = Math.Min(one.Right, other.Right) - Math.Max(one.X, other.X) + (2 * Clearance);
        var height = Math.Min(one.Bottom, other.Bottom) - Math.Max(one.Y, other.Y) + (2 * Clearance);

        return width > 0 && height > 0 ? width * height : 0;
    }

    private static IEnumerable<LinkRoute> Detours(GraphRect from, GraphRect to, List<GraphRect> blocking, double lane)
    {
        var (top, bottom) = (Math.Min(from.Center.Y, to.Center.Y), Math.Max(from.Center.Y, to.Center.Y));
        var (left, right) = (Math.Min(from.Center.X, to.Center.X), Math.Max(from.Center.X, to.Center.X));
        var column = blocking.Where(other => other.Y < bottom && other.Bottom > top).Append(from).Append(to).ToList();
        var row = blocking.Where(other => other.X < right && other.Right > left).Append(from).Append(to).ToList();
        var outward = Math.Max(0, lane >= 0 ? 2 * lane : (-2 * lane) - LinkLanes.Gap);
        var (downward, rightward) = (to.Center.Y >= from.Center.Y ? 1 : -1, to.Center.X >= from.Center.X ? 1 : -1);
        var east = column.Max(rect => rect.Right) + LaneGap + outward;
        var west = column.Min(rect => rect.X) - LaneGap - outward;
        var north = row.Min(rect => rect.Y) - LaneGap - outward;
        var south = row.Max(rect => rect.Bottom) + LaneGap + outward;
        var (y, ty) = (from.Center.Y - (downward * Spread(from.Height, outward)), to.Center.Y + (downward * Spread(to.Height, outward)));
        var (x, tx) = (from.Center.X - (rightward * Spread(from.Width, outward)), to.Center.X + (rightward * Spread(to.Width, outward)));

        yield return new LinkRoute([new(from.Right, y), new(east, y), new(east, ty), new(to.Right, ty)]);
        yield return new LinkRoute([new(from.X, y), new(west, y), new(west, ty), new(to.X, ty)]);
        yield return new LinkRoute([new(x, from.Y), new(x, north), new(tx, north), new(tx, to.Y)]);
        yield return new LinkRoute([new(x, from.Bottom), new(x, south), new(tx, south), new(tx, to.Bottom)]);
    }

    private static double Spread(double size, double outward) => ((size / 2) - 4) * outward / (outward + (size / 2));

    private static GraphPoint Inside(GraphRect rect, GraphPoint point) =>
        new(Math.Clamp(point.X, rect.X + 2, rect.Right - 2), Math.Clamp(point.Y, rect.Y + 2, rect.Bottom - 2));

    private static bool Clear(LinkRoute route, List<GraphRect> blocking, double clearance) =>
        !route.Points.Zip(route.Points.Skip(1)).Any(segment => blocking.Any(rect => Cuts(segment.First, segment.Second, rect, clearance)));

    private static bool Apart(LinkRoute route, GraphRect from, GraphRect to)
    {
        var segments = route.Points.Zip(route.Points.Skip(1)).ToList();

        return segments.Select((segment, index) => (index == 0 || !Cuts(segment.First, segment.Second, from, -1)) && (index == segments.Count - 1 || !Cuts(segment.First, segment.Second, to, -1))).All(apart => apart);
    }

    private static GraphPoint Exit(GraphRect rect, GraphPoint origin, GraphPoint toward)
    {
        var (dx, dy) = (toward.X - origin.X, toward.Y - origin.Y);

        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return origin;
        }

        var tx = dx > 0 ? (rect.Right - origin.X) / dx : dx < 0 ? (rect.X - origin.X) / dx : double.MaxValue;
        var ty = dy > 0 ? (rect.Bottom - origin.Y) / dy : dy < 0 ? (rect.Y - origin.Y) / dy : double.MaxValue;
        var t = Math.Min(tx, ty);

        return new GraphPoint(origin.X + (dx * t), origin.Y + (dy * t));
    }

    private static bool Cuts(GraphPoint from, GraphPoint to, GraphRect rect, double clearance)
    {
        var box = new GraphRect(rect.X - clearance, rect.Y - clearance, rect.Width + (2 * clearance), rect.Height + (2 * clearance));
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
