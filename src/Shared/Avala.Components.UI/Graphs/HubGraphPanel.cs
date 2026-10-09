using Avala.Components.Graphs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Graphs;

public sealed class HubGraphPanel : Panel
{
    private HubGraphLayout layout = HubGraphLayout.Empty;
    private OrbitPanel?[] orbits = [];
    private readonly EdgeLayer edges;

    public HubGraphPanel()
    {
        edges = new EdgeLayer(this, Draw);
        VisualChildren.Add(edges);
    }

    public HubGraphLayout Layout => layout;

    private void Draw(DrawingContext context, EdgePens pens)
    {
        for (var hub = 0; hub < layout.Hubs.Count && hub < orbits.Length; hub++)
        {
            var placement = layout.Hubs[hub];

            for (var satellite = 0; satellite < placement.Satellites.Count; satellite++)
            {
                var kind = orbits[hub] is { } orbit && satellite < orbit.Children.Count ? Graph.Read(orbit.Children[satellite], Graph.EdgeProperty) : EdgeKind.Quiet;
                context.DrawGeometry(null, pens.For(kind), Edge(placement.Center, placement.Radius, placement.Satellites[satellite]));
            }
        }
    }

    internal static StreamGeometry Edge(GraphPoint hub, double radius, GraphRect satellite)
    {
        var target = satellite.Center;
        var end = target.X > hub.X + radius && satellite.X > hub.X ? new Point(satellite.X, target.Y)
            : target.X < hub.X - radius && satellite.Right < hub.X ? new Point(satellite.Right, target.Y)
            : target.Y < hub.Y ? new Point(target.X, satellite.Bottom)
            : new Point(target.X, satellite.Y);
        var angle = Math.Atan2(end.Y - hub.Y, end.X - hub.X);
        var start = new Point(hub.X + (radius * Math.Cos(angle)), hub.Y + (radius * Math.Sin(angle)));
        var control = new Point((start.X + end.X) / 2, end.Y);
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(start, false);
            stream.QuadraticBezierTo(control, end);
            stream.EndFigure(false);
        }

        return geometry;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        orbits = [.. Children.Select(child => child.GetVisualDescendants().OfType<OrbitPanel>().FirstOrDefault())];
        layout = HubLayout.Arrange(
            [.. Children.Select((child, index) => new HubSpec(Math.Max(child.DesiredSize.Width, child.DesiredSize.Height) / 2, orbits[index]?.Sizes ?? []))],
            new HubSpacing());

        return new Size(layout.Size.Width, layout.Size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var dx = Math.Max(0, (finalSize.Width - layout.Size.Width) / 2);
        var dy = Math.Max(0, (finalSize.Height - layout.Size.Height) / 2);

        if (dx > 0 || dy > 0)
        {
            layout = layout with
            {
                Hubs = [.. layout.Hubs.Select(hub => hub with
                {
                    Center = hub.Center.Offset(dx, dy),
                    Satellites = [.. hub.Satellites.Select(rect => rect with { X = rect.X + dx, Y = rect.Y + dy })],
                })],
                Size = new GraphSize(finalSize.Width, finalSize.Height),
            };
        }

        for (var index = 0; index < Children.Count && index < layout.Hubs.Count; index++)
        {
            var child = Children[index];
            var hub = layout.Hubs[index];
            orbits[index]?.Place([.. hub.Satellites.Select(rect => rect with { X = rect.X - hub.Center.X, Y = rect.Y - hub.Center.Y })]);
            child.Arrange(new Rect(new Point(hub.Center.X - (child.DesiredSize.Width / 2), hub.Center.Y - (child.DesiredSize.Height / 2)), child.DesiredSize));
        }

        edges.Fit(finalSize);

        return finalSize;
    }
}
