namespace Avala.Components.Graphs;

public readonly record struct GraphPoint(double X, double Y)
{
    public GraphPoint Offset(double x, double y) => new(X + x, Y + y);
}

public readonly record struct GraphSize(double Width, double Height);

public readonly record struct GraphRect(double X, double Y, double Width, double Height)
{
    public GraphPoint Center => new(X + (Width / 2), Y + (Height / 2));

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public static GraphRect Around(GraphPoint center, GraphSize size) =>
        new(center.X - (size.Width / 2), center.Y - (size.Height / 2), size.Width, size.Height);

    public bool Overlaps(GraphRect other, double margin) =>
        X < other.Right + margin && other.X < Right + margin && Y < other.Bottom + margin && other.Y < Bottom + margin;
}

public readonly record struct GraphCurve(GraphPoint Start, GraphPoint Control, GraphPoint End)
{
    public GraphPoint At(double t)
    {
        var (a, b, c) = ((1 - t) * (1 - t), 2 * (1 - t) * t, t * t);

        return new GraphPoint((a * Start.X) + (b * Control.X) + (c * End.X), (a * Start.Y) + (b * Control.Y) + (c * End.Y));
    }
}

public sealed record HubSpec(double Radius, IReadOnlyList<GraphSize> Satellites);

public sealed record HubPlacement(GraphPoint Center, double Radius, IReadOnlyList<GraphRect> Satellites);

public sealed record HubGraphLayout(IReadOnlyList<HubPlacement> Hubs, GraphSize Size, GraphRect Frame)
{
    public static HubGraphLayout Empty { get; } = new([], new GraphSize(0, 0), default);
}

public sealed record HubSpacing(double Ring = 220, double Gap = 36, double Margin = 12, double Padding = 48, double Step = 14);

public static class HubLayout
{
    private const int Spreads = 8;

    public static HubGraphLayout Arrange(IReadOnlyList<HubSpec> hubs, HubSpacing spacing) => Arrange(hubs, spacing, default);

    public static HubGraphLayout Arrange(IReadOnlyList<HubSpec> hubs, HubSpacing spacing, GraphRect frame)
    {
        if (hubs.Count == 0)
        {
            return HubGraphLayout.Empty;
        }

        var centers = Centers(hubs, spacing);
        var drawing = new Drawing([.. hubs.Select((hub, index) => Square(centers[index], hub.Radius))]);
        var satellites = new List<GraphRect>[hubs.Count];

        for (var index = 0; index < hubs.Count; index++)
        {
            satellites[index] = Around(hubs[index], centers[index], hubs.Count == 1, drawing, spacing);
        }

        return Normalized(hubs, centers, satellites, Framed(hubs, centers, satellites, spacing.Padding, frame));
    }

    public static IReadOnlyList<double> Angles(int count, double outward, bool allAround) =>
        [.. Enumerable.Range(0, count).Select(index => outward + Offset(index, allAround ? 45 : 36, allAround ? 180 : 108))];

    public static GraphCurve Edge(GraphPoint hub, double radius, GraphRect satellite)
    {
        var target = satellite.Center;
        var end = target.X > hub.X + radius && satellite.X > hub.X ? new GraphPoint(satellite.X, target.Y)
            : target.X < hub.X - radius && satellite.Right < hub.X ? new GraphPoint(satellite.Right, target.Y)
            : target.Y < hub.Y ? new GraphPoint(target.X, satellite.Bottom)
            : new GraphPoint(target.X, satellite.Y);
        var angle = Math.Atan2(end.Y - hub.Y, end.X - hub.X);
        var start = hub.Offset(radius * Math.Cos(angle), radius * Math.Sin(angle));

        return new GraphCurve(start, new GraphPoint((start.X + end.X) / 2, end.Y), end);
    }

    private static double Offset(int index, double step, double half)
    {
        var turn = (index + 1) / 2;
        var sign = index % 2 == 1 ? 1 : -1;
        var reach = turn * step;
        var lap = Math.Floor(reach / (half + 0.001));

        return sign * ((reach - (lap * half)) + (lap * step / 2));
    }

    private static GraphPoint[] Centers(IReadOnlyList<HubSpec> hubs, HubSpacing spacing)
    {
        if (hubs.Count == 1)
        {
            return [new GraphPoint(0, 0)];
        }

        var ring = spacing.Ring * Math.Max(1, hubs.Count / 4.0);
        var slots = Enumerable.Range(0, hubs.Count)
            .Select(slot => 180 + (360.0 * slot / hubs.Count))
            .OrderByDescending(angle => Math.Round(Math.Abs(Math.Cos(Radians(angle))), 6))
            .ToArray();
        var order = Enumerable.Range(0, hubs.Count)
            .OrderByDescending(index => hubs[index].Satellites.Count)
            .ThenBy(index => index)
            .ToArray();
        var centers = new GraphPoint[hubs.Count];

        for (var rank = 0; rank < order.Length; rank++)
        {
            var angle = Radians(slots[rank]);
            centers[order[rank]] = new GraphPoint(ring * Math.Cos(angle), ring * Math.Sin(angle));
        }

        return centers;
    }

    private static List<GraphRect> Around(HubSpec hub, GraphPoint center, bool allAround, Drawing drawing, HubSpacing spacing)
    {
        var angles = Angles(hub.Satellites.Count, allAround ? 180 : Outward(center), allAround);
        var orbit = new Orbit(hub, center, drawing, spacing);

        for (var spread = 0; spread < Spreads; spread++)
        {
            var rects = orbit.Clear(angles, spacing.Gap + (spread * spacing.Ring / 4));

            if (rects.Count == hub.Satellites.Count)
            {
                return rects;
            }
        }

        return orbit.Apart(angles);
    }

    internal static (GraphRect Rect, GraphCurve Edge) Candidate(HubSpec hub, GraphPoint center, GraphSize size, double degrees, double gap)
    {
        var angle = Radians(degrees);
        var rect = At(center, angle, hub.Radius + gap + Support(size, angle), size);

        return (rect, Edge(center, hub.Radius, rect));
    }

    private static GraphRect Framed(IReadOnlyList<HubSpec> hubs, GraphPoint[] centers, List<GraphRect>[] satellites, double padding, GraphRect frame)
    {
        var all = hubs.Select((hub, index) => Square(centers[index], hub.Radius)).Concat(satellites.SelectMany(rects => rects)).ToList();
        var (left, top) = (all.Min(rect => rect.X) - padding, all.Min(rect => rect.Y) - padding);
        var (right, bottom) = (all.Max(rect => rect.Right) + padding, all.Max(rect => rect.Bottom) + padding);

        if (frame == default)
        {
            return new GraphRect(left, top, right - left, bottom - top);
        }

        var middle = frame.Center;
        var wide = Math.Max(frame.Width / 2, Math.Max(middle.X - left, right - middle.X));
        var tall = Math.Max(frame.Height / 2, Math.Max(middle.Y - top, bottom - middle.Y));

        return new GraphRect(middle.X - wide, middle.Y - tall, wide * 2, tall * 2);
    }

    private static HubGraphLayout Normalized(IReadOnlyList<HubSpec> hubs, GraphPoint[] centers, List<GraphRect>[] satellites, GraphRect frame) =>
        new(
            [
                .. hubs.Select((hub, index) => new HubPlacement(
                    centers[index].Offset(-frame.X, -frame.Y),
                    hub.Radius,
                    [.. satellites[index].Select(rect => rect with { X = rect.X - frame.X, Y = rect.Y - frame.Y })])),
            ],
            new GraphSize(frame.Width, frame.Height),
            frame);

    private static double Outward(GraphPoint center)
    {
        var angle = Math.Atan2(center.Y, center.X);

        return Math.Abs(Math.Cos(angle)) < 0.5 ? 0 : angle * 180 / Math.PI;
    }

    private static GraphRect At(GraphPoint center, double angle, double distance, GraphSize size) =>
        GraphRect.Around(center.Offset(distance * Math.Cos(angle), distance * Math.Sin(angle)), size);

    internal static GraphRect Square(GraphPoint center, double radius) => GraphRect.Around(center, new GraphSize(radius * 2, radius * 2));

    private static double Support(GraphSize size, double angle)
    {
        var cos = Math.Abs(Math.Cos(angle));
        var sin = Math.Abs(Math.Sin(angle));
        var across = cos < 1e-6 ? double.PositiveInfinity : size.Width / 2 / cos;
        var along = sin < 1e-6 ? double.PositiveInfinity : size.Height / 2 / sin;

        return Math.Min(across, along);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
