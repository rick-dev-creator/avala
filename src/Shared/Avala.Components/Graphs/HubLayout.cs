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

public sealed record HubSpec(double Radius, IReadOnlyList<GraphSize> Satellites);

public sealed record HubPlacement(GraphPoint Center, double Radius, IReadOnlyList<GraphRect> Satellites);

public sealed record HubGraphLayout(IReadOnlyList<HubPlacement> Hubs, GraphSize Size)
{
    public static HubGraphLayout Empty { get; } = new([], new GraphSize(0, 0));
}

public sealed record HubSpacing(double Ring = 220, double Gap = 36, double Margin = 12, double Padding = 24, double Step = 14);

public static class HubLayout
{
    private const int MaxPushes = 600;

    public static HubGraphLayout Arrange(IReadOnlyList<HubSpec> hubs, HubSpacing spacing)
    {
        if (hubs.Count == 0)
        {
            return HubGraphLayout.Empty;
        }

        var centers = Centers(hubs, spacing);
        var placed = new List<GraphRect>(hubs.Select((hub, index) => Square(centers[index], hub.Radius)));
        var satellites = new List<GraphRect>[hubs.Count];

        for (var index = 0; index < hubs.Count; index++)
        {
            satellites[index] = Orbit(hubs[index], centers[index], hubs.Count == 1, placed, spacing);
        }

        return Normalized(hubs, centers, satellites, spacing.Padding);
    }

    public static IReadOnlyList<double> Angles(int count, double outward, bool allAround) =>
        [.. Enumerable.Range(0, count).Select(index => outward + Offset(index, allAround ? 45 : 36, allAround ? 180 : 108))];

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

    private static List<GraphRect> Orbit(HubSpec hub, GraphPoint center, bool allAround, List<GraphRect> placed, HubSpacing spacing)
    {
        var angles = Angles(hub.Satellites.Count, allAround ? 180 : Outward(center), allAround);
        var rects = new List<GraphRect>(hub.Satellites.Count);

        for (var index = 0; index < hub.Satellites.Count; index++)
        {
            var size = hub.Satellites[index];
            var angle = Radians(angles[index]);
            var distance = hub.Radius + spacing.Gap + Support(size, angle);
            var rect = At(center, angle, distance, size);

            for (var push = 0; push < MaxPushes && placed.Any(other => other.Overlaps(rect, spacing.Margin)); push++)
            {
                distance += spacing.Step;
                rect = At(center, angle, distance, size);
            }

            placed.Add(rect);
            rects.Add(rect);
        }

        return rects;
    }

    private static HubGraphLayout Normalized(IReadOnlyList<HubSpec> hubs, GraphPoint[] centers, List<GraphRect>[] satellites, double padding)
    {
        var all = hubs.Select((hub, index) => Square(centers[index], hub.Radius)).Concat(satellites.SelectMany(rects => rects)).ToList();
        var wide = Math.Max(-all.Min(rect => rect.X), all.Max(rect => rect.Right)) + padding;
        var tall = Math.Max(-all.Min(rect => rect.Y), all.Max(rect => rect.Bottom)) + padding;
        var (left, top, right, bottom) = (-wide, -tall, wide, tall);

        return new HubGraphLayout(
            [
                .. hubs.Select((hub, index) => new HubPlacement(
                    centers[index].Offset(-left, -top),
                    hub.Radius,
                    [.. satellites[index].Select(rect => rect with { X = rect.X - left, Y = rect.Y - top })])),
            ],
            new GraphSize(right - left, bottom - top));
    }

    private static double Outward(GraphPoint center)
    {
        var angle = Math.Atan2(center.Y, center.X);

        return Math.Abs(Math.Cos(angle)) < 0.5 ? 0 : angle * 180 / Math.PI;
    }

    private static GraphRect At(GraphPoint center, double angle, double distance, GraphSize size) =>
        GraphRect.Around(center.Offset(distance * Math.Cos(angle), distance * Math.Sin(angle)), size);

    private static GraphRect Square(GraphPoint center, double radius) => GraphRect.Around(center, new GraphSize(radius * 2, radius * 2));

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
