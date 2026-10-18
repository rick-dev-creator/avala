using System.Globalization;
using Avala.Components.Graphs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Avala.Components.UI.Graphs;

public sealed class C4GraphPanel : Panel
{
    public static readonly StyledProperty<IReadOnlyList<GraphLink>> LinksProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphLink>>(nameof(Links), []);

    public static readonly StyledProperty<IReadOnlyList<GraphLink>> StructureProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphLink>>(nameof(Structure), []);

    public static readonly StyledProperty<IReadOnlyList<GraphLink>> FlowsProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphLink>>(nameof(Flows), []);

    public static readonly StyledProperty<IReadOnlyList<GraphFrame>> FramesProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphFrame>>(nameof(Frames), []);

    public static readonly StyledProperty<IBrush> LabelBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(LabelBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> LabelPlateBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(LabelPlateBrush), Brushes.Transparent);

    public static readonly StyledProperty<double> LabelSizeProperty =
        AvaloniaProperty.Register<C4GraphPanel, double>(nameof(LabelSize), 11.5);

    public static readonly StyledProperty<IBrush> FrameLabelBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(FrameLabelBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> BadgeBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(BadgeBrush), Brushes.SlateBlue);

    public static readonly StyledProperty<IBrush> BadgeTextBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(BadgeTextBrush), Brushes.White);

    private const double Room = 12;

    private const double LabelWidth = 200;

    private const double BadgeRadius = 9;

    private static readonly LayeredSpacing Spacing = new();

    private static readonly Thickness PlatePadding = new(6, 2);

    private readonly EdgeLayer edges;
    private LayeredGraphLayout layout = LayeredGraphLayout.Empty;
    private Dictionary<string, int> keys = [];
    private Vector origin;

    static C4GraphPanel()
    {
        AffectsMeasure<C4GraphPanel>(StructureProperty, FlowsProperty, FramesProperty, LabelSizeProperty);
        LinksProperty.Changed.AddClassHandler<C4GraphPanel>((panel, _) => EdgeLayer.Refresh(panel));
    }

    public C4GraphPanel()
    {
        edges = new EdgeLayer(this, Draw);
        VisualChildren.Add(edges);
    }

    public IReadOnlyList<GraphLink> Links
    {
        get => GetValue(LinksProperty);
        set => SetValue(LinksProperty, value);
    }

    public IReadOnlyList<GraphLink> Structure
    {
        get => GetValue(StructureProperty);
        set => SetValue(StructureProperty, value);
    }

    public IReadOnlyList<GraphLink> Flows
    {
        get => GetValue(FlowsProperty);
        set => SetValue(FlowsProperty, value);
    }

    public IReadOnlyList<GraphFrame> Frames
    {
        get => GetValue(FramesProperty);
        set => SetValue(FramesProperty, value);
    }

    public IBrush LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush LabelPlateBrush
    {
        get => GetValue(LabelPlateBrushProperty);
        set => SetValue(LabelPlateBrushProperty, value);
    }

    public double LabelSize
    {
        get => GetValue(LabelSizeProperty);
        set => SetValue(LabelSizeProperty, value);
    }

    public IBrush FrameLabelBrush
    {
        get => GetValue(FrameLabelBrushProperty);
        set => SetValue(FrameLabelBrushProperty, value);
    }

    public IBrush BadgeBrush
    {
        get => GetValue(BadgeBrushProperty);
        set => SetValue(BadgeBrushProperty, value);
    }

    public IBrush BadgeTextBrush
    {
        get => GetValue(BadgeTextBrushProperty);
        set => SetValue(BadgeTextBrushProperty, value);
    }

    public LayeredGraphLayout Layout => layout;

    public IReadOnlyList<Rect> Labels => [.. Placed(Links).Select(placed => placed.Plate.Translate(origin))];

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        var frames = Frames.Select((frame, index) => (frame.Key, index)).DistinctBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.index, StringComparer.Ordinal);
        keys = Children.Select((child, index) => (Key: Graph.Read(child, Graph.KeyProperty), index)).Where(pair => pair.Key.Length > 0).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        layout = LayeredLayout.Arrange(
            [.. Children.Select(child => new LayeredNodeSpec(new GraphSize(child.DesiredSize.Width, child.DesiredSize.Height)) { Group = frames.TryGetValue(Graph.Read(child, Graph.FrameProperty), out var group) ? group : -1 })],
            [.. Structure.Where(link => keys.ContainsKey(link.From) && keys.ContainsKey(link.To)).Select(link => new LayeredLink(keys[link.From], keys[link.To]))],
            Frames.Count,
            Spacing);
        origin = default;
        var drawn = layout.Nodes.Concat(layout.Groups.Where(group => group.Width > 0)).Select(Rect).Concat(Placed([.. Structure, .. Flows]).SelectMany(placed => new[] { placed.Plate, Extent(placed.Route) })).ToList();

        if (drawn.Count == 0)
        {
            return default;
        }

        var bounds = drawn.Aggregate((all, rect) => all.Union(rect)).Inflate(Room);
        origin = new Vector(-bounds.X, -bounds.Y);

        return bounds.Size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < Children.Count && index < layout.Nodes.Count; index++)
        {
            Children[index].Arrange(Rect(layout.Nodes[index]).Translate(origin));
        }

        edges.Fit(finalSize);

        return finalSize;
    }

    private void Draw(DrawingContext context, EdgePens pens)
    {
        using var shifted = context.PushTransform(Matrix.CreateTranslation(origin));
        var quiet = pens.For(EdgeKind.Waiting);

        for (var index = 0; index < Frames.Count && index < layout.Groups.Count; index++)
        {
            var frame = layout.Groups[index];

            if (frame.Width > 0)
            {
                context.DrawRectangle(null, quiet, new RoundedRect(Rect(frame), 12));
                var label = Text(Frames[index].Label, FrameLabelBrush, frame.Width - 24);
                context.DrawText(label, new Point(frame.X + 12, frame.Bottom - label.Height - 6));
            }
        }

        var placed = Placed(Links);

        foreach (var (link, route, _, _) in placed)
        {
            var pen = pens.For(link.Kind);
            var points = route.Points.Select(point => new Point(point.X, point.Y)).ToList();

            for (var index = 1; index < points.Count; index++)
            {
                context.DrawLine(pen, points[index - 1], points[index]);
            }

            Arrow(context, pen, points[^2], points[^1]);
        }

        foreach (var (link, _, plate, label) in placed.Where(placed => placed.Plate.Width > 0))
        {
            context.DrawRectangle(LabelPlateBrush, null, new RoundedRect(plate, plate.Height / 2));
            var left = plate.X + PlatePadding.Left;

            if (link.Badge.Length > 0)
            {
                var center = new Point(left + BadgeRadius, plate.Center.Y);
                context.DrawEllipse(BadgeBrush, null, center, BadgeRadius, BadgeRadius);
                var badge = Text(link.Badge, BadgeTextBrush, LabelWidth);
                context.DrawText(badge, new Point(center.X - (badge.Width / 2), center.Y - (badge.Height / 2)));
                left += (2 * BadgeRadius) + 5;
            }

            if (label is not null)
            {
                context.DrawText(label, new Point(left, plate.Center.Y - (label.Height / 2)));
            }
        }
    }

    private List<(GraphLink Link, LinkRoute Route, Rect Plate, FormattedText? Label)> Placed(IReadOnlyList<GraphLink> links)
    {
        var nodes = layout.Nodes;
        var taken = new List<GraphRect>([.. nodes, .. layout.Groups.Where(group => group.Width > 0).SelectMany(Borders), .. FrameLabels()]);
        var placed = new List<(GraphLink, LinkRoute, Rect, FormattedText?)>();

        foreach (var link in links.Where(link => keys.TryGetValue(link.From, out var from) && keys.TryGetValue(link.To, out var to) && from < nodes.Count && to < nodes.Count && from != to))
        {
            var (from, to) = (nodes[keys[link.From]], nodes[keys[link.To]]);
            var (dx, dy) = LinkLanes.Shift(links, link, (from.Center.X, from.Center.Y), (to.Center.X, to.Center.Y));
            var route = LinkRoutes.Route(from, to, nodes, new GraphPoint(dx, dy));
            var label = link.Label.Length > 0 ? Text(link.Label, LabelBrush, LabelWidth) : null;
            var width = (label?.Width ?? 0) + (link.Badge.Length > 0 ? (2 * BadgeRadius) + (label is null ? 0 : 5) : 0);
            var height = Math.Max(label?.Height ?? 0, link.Badge.Length > 0 ? 2 * BadgeRadius : 0);

            if (width <= 0)
            {
                placed.Add((link, route, default, null));
                continue;
            }

            var plate = LinkRoutes.Label(route, new GraphSize(width + PlatePadding.Left + PlatePadding.Right, height + PlatePadding.Top + PlatePadding.Bottom), taken);
            taken.Add(plate);
            placed.Add((link, route, Rect(plate), label));
        }

        return placed;
    }

    private static void Arrow(DrawingContext context, IPen pen, Point start, Point end)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);

        foreach (var side in new[] { -0.45, 0.45 })
        {
            context.DrawLine(pen, end, new Point(end.X - (7 * Math.Cos(angle + side)), end.Y - (7 * Math.Sin(angle + side))));
        }
    }

    private static Rect Rect(GraphRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private IEnumerable<GraphRect> FrameLabels()
    {
        for (var index = 0; index < Frames.Count && index < layout.Groups.Count; index++)
        {
            var frame = layout.Groups[index];

            if (frame.Width > 0)
            {
                var label = Text(Frames[index].Label, FrameLabelBrush, frame.Width - 24);

                yield return new GraphRect(frame.X + 12, frame.Bottom - label.Height - 6, label.Width, label.Height);
            }
        }
    }

    private static GraphRect[] Borders(GraphRect frame) =>
    [
        new(frame.X, frame.Y, frame.Width, 0),
        new(frame.X, frame.Bottom, frame.Width, 0),
        new(frame.X, frame.Y, 0, frame.Height),
        new(frame.Right, frame.Y, 0, frame.Height),
    ];

    private static Rect Extent(LinkRoute route)
    {
        var (left, top) = (route.Points.Min(point => point.X), route.Points.Min(point => point.Y));

        return new Rect(left, top, route.Points.Max(point => point.X) - left, route.Points.Max(point => point.Y) - top);
    }

    private FormattedText Text(string text, IBrush brush, double width) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(TextElement.GetFontFamily(this)), LabelSize, brush)
        {
            MaxTextWidth = Math.Max(1, width),
            TextAlignment = TextAlignment.Left,
        };
}
