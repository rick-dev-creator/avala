using System.Globalization;
using Avala.Components.Graphs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Graphs;

public sealed class C4GraphPanel : Panel
{
    public static readonly StyledProperty<IReadOnlyList<GraphLink>> LinksProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphLink>>(nameof(Links), []);

    public static readonly StyledProperty<IReadOnlyList<GraphLink>> StructureProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphLink>>(nameof(Structure), []);

    public static readonly StyledProperty<IReadOnlyList<GraphFrame>> FramesProperty =
        AvaloniaProperty.Register<C4GraphPanel, IReadOnlyList<GraphFrame>>(nameof(Frames), []);

    public static readonly StyledProperty<IBrush> LabelBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(LabelBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> BadgeBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(BadgeBrush), Brushes.SlateBlue);

    public static readonly StyledProperty<IBrush> BadgeTextBrushProperty =
        AvaloniaProperty.Register<C4GraphPanel, IBrush>(nameof(BadgeTextBrush), Brushes.White);

    private static readonly LayeredSpacing Spacing = new();

    private const double LabelRoom = 180;

    private readonly EdgeLayer edges;
    private LayeredGraphLayout layout = LayeredGraphLayout.Empty;
    private Dictionary<string, int> keys = [];

    static C4GraphPanel()
    {
        AffectsMeasure<C4GraphPanel>(StructureProperty, FramesProperty);
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

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        var frames = Frames.Select((frame, index) => (frame.Key, index)).ToDictionary(pair => pair.Key, pair => pair.index, StringComparer.Ordinal);
        keys = Children.Select((child, index) => (Key: Graph.Read(child, Graph.KeyProperty), index)).Where(pair => pair.Key.Length > 0).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);
        layout = LayeredLayout.Arrange(
            [.. Children.Select(child => new LayeredNodeSpec(new GraphSize(child.DesiredSize.Width, child.DesiredSize.Height)) { Group = frames.TryGetValue(Graph.Read(child, Graph.FrameProperty), out var group) ? group : -1 })],
            [.. Structure.Where(link => keys.ContainsKey(link.From) && keys.ContainsKey(link.To)).Select(link => new LayeredLink(keys[link.From], keys[link.To]))],
            Frames.Count,
            Spacing);

        return new Size(layout.Size.Width + LabelRoom, layout.Size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < Children.Count && index < layout.Nodes.Count; index++)
        {
            var bounds = layout.Nodes[index];
            Children[index].Arrange(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        edges.Fit(finalSize);

        return finalSize;
    }

    private void Draw(DrawingContext context, EdgePens pens)
    {
        var quiet = pens.For(EdgeKind.Waiting);

        for (var index = 0; index < Frames.Count && index < layout.Groups.Count; index++)
        {
            var frame = layout.Groups[index];

            if (frame.Width > 0)
            {
                context.DrawRectangle(null, quiet, new RoundedRect(new Rect(frame.X, frame.Y, frame.Width, frame.Height), 12));
                var label = Text(Frames[index].Label, 11);
                context.DrawText(label, new Point(frame.X + 12, frame.Bottom - label.Height - 6));
            }
        }

        foreach (var link in Links.Where(link => keys.ContainsKey(link.From) && keys.ContainsKey(link.To) && keys[link.From] < layout.Nodes.Count && keys[link.To] < layout.Nodes.Count))
        {
            Link(context, pens.For(link.Kind), link);
        }
    }

    private void Link(DrawingContext context, IPen pen, GraphLink link)
    {
        var from = layout.Nodes[keys[link.From]];
        var to = layout.Nodes[keys[link.To]];
        var edgeStart = Edge(from, Center(to));
        var edgeEnd = Edge(to, Center(from));
        var (dx, dy) = LinkLanes.Shift(Links, link, (edgeStart.X, edgeStart.Y), (edgeEnd.X, edgeEnd.Y));
        var start = new Point(edgeStart.X + dx, edgeStart.Y + dy);
        var end = new Point(edgeEnd.X + dx, edgeEnd.Y + dy);
        context.DrawLine(pen, start, end);
        Arrow(context, pen, start, end);
        var middle = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);

        if (link.Label.Length > 0)
        {
            var label = Text(link.Label, 10.5);
            context.DrawText(label, new Point(dx < 0 ? middle.X - 6 - label.Width : middle.X + 6, dy > 0 ? middle.Y + 2 : middle.Y - 14));
        }

        if (link.Badge.Length > 0)
        {
            context.DrawEllipse(BadgeBrush, null, middle, 9, 9);
            var badge = Text(link.Badge, 10.5, BadgeTextBrush);
            context.DrawText(badge, new Point(middle.X - (badge.Width / 2), middle.Y - (badge.Height / 2)));
        }
    }

    private static void Arrow(DrawingContext context, IPen pen, Point start, Point end)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);

        foreach (var side in new[] { -0.45, 0.45 })
        {
            context.DrawLine(pen, end, new Point(end.X - (7 * Math.Cos(angle + side)), end.Y - (7 * Math.Sin(angle + side))));
        }
    }

    private static Point Center(GraphRect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    private static Point Edge(GraphRect rect, Point toward)
    {
        var center = Center(rect);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;

        if (dx == 0 && dy == 0)
        {
            return center;
        }

        var scale = Math.Min(Math.Abs(dx) < 0.001 ? double.MaxValue : rect.Width / 2 / Math.Abs(dx), Math.Abs(dy) < 0.001 ? double.MaxValue : rect.Height / 2 / Math.Abs(dy));

        return new Point(center.X + (dx * scale), center.Y + (dy * scale));
    }

    private FormattedText Text(string text, double size, IBrush? brush = null) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, brush ?? LabelBrush);
}
