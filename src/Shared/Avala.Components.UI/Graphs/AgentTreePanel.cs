using Avala.Components.Graphs;
using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;

namespace Avala.Components.UI.Graphs;

public sealed class AgentTreePanel : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<AgentTreePanel, double>(nameof(Gap), 12);

    public static readonly StyledProperty<double> IndentProperty =
        AvaloniaProperty.Register<AgentTreePanel, double>(nameof(Indent), 24);

    public static readonly StyledProperty<double> DropProperty =
        AvaloniaProperty.Register<AgentTreePanel, double>(nameof(Drop), 28);

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<AgentTreePanel, double>(nameof(CardWidth));

    public static readonly StyledProperty<double> RootWidthProperty =
        AvaloniaProperty.Register<AgentTreePanel, double>(nameof(RootWidth));

    public static readonly StyledProperty<IBrush> JointBrushProperty =
        AvaloniaProperty.Register<AgentTreePanel, IBrush>(nameof(JointBrush), Brushes.Transparent);

    private const double JointRadius = 3.5;

    private const double Corner = 6;

    private const double MinimumCardWidth = 200;

    private const double MinimumIndent = 8;

    private const double ReadableCardWidth = 240;

    private static readonly double[] Narrower = [0.9, 0.8, 0.72];

    private static readonly AvaloniaProperty[] Strokes = [EdgeBrushes.FlowingProperty, EdgeBrushes.AttentionProperty, EdgeBrushes.WaitingProperty, EdgeBrushes.FailureProperty, EdgeBrushes.QuietProperty];

    private AgentTreeGraphLayout layout = AgentTreeGraphLayout.Empty;
    private AgentTreeRoutes routes = AgentTreeRoutes.Empty;
    private StreamGeometry?[] shapes = [];
    private readonly List<(AgentTreeMotion.Ghost Ghost, IReadOnlyList<GraphPoint> Route, StreamGeometry? Shape)> ghosts = [];
    private readonly AgentTreeMotion motion = new();
    private readonly AgentTreeStrokes strokes = new();
    private readonly EdgeLayer edges;
    private readonly EdgeLayer joints;
    private readonly Action<TimeSpan> frame;
    private double arrangedWidth = double.NaN;
    private bool framing;
    private bool loops;

    static AgentTreePanel()
    {
        AffectsMeasure<AgentTreePanel>(GapProperty, IndentProperty, DropProperty, CardWidthProperty, RootWidthProperty);
        JointBrushProperty.Changed.AddClassHandler<AgentTreePanel>((panel, _) => EdgeLayer.Refresh(panel));
    }

    public AgentTreePanel()
    {
        edges = new EdgeLayer(this, DrawEdges);
        joints = new EdgeLayer(this, DrawJoints) { ZIndex = int.MaxValue };
        frame = Frame;
        VisualChildren.Add(edges);
        VisualChildren.Add(joints);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double Indent
    {
        get => GetValue(IndentProperty);
        set => SetValue(IndentProperty, value);
    }

    public double Drop
    {
        get => GetValue(DropProperty);
        set => SetValue(DropProperty, value);
    }

    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double RootWidth
    {
        get => GetValue(RootWidthProperty);
        set => SetValue(RootWidthProperty, value);
    }

    public IBrush JointBrush
    {
        get => GetValue(JointBrushProperty);
        set => SetValue(JointBrushProperty, value);
    }

    public AgentTreeGraphLayout Layout => layout;

    public AgentTreeRoutes Drawn => routes with { Leaving = [.. ghosts.Select(ghost => ghost.Ghost.Rect)] };

    protected override Size MeasureOverride(Size availableSize)
    {
        Fit(availableSize.Width);

        return new Size(layout.Size.Width, layout.Size.Height);
    }

    private void Fit(double available)
    {
        var widths = Children.Select(CardWidthOf).ToList();
        layout = Lay(widths, available, Indent, railsOnly: false);

        if (layout.Shape == AgentTreeShape.Tree || !double.IsFinite(available) || CardWidth <= 0 || layout.Nodes.Count == 0)
        {
            return;
        }

        if (Best(widths, available, layout) is { } best)
        {
            layout = best;

            return;
        }

        layout = Lay(widths, available, Indent, railsOnly: true);

        var steps = layout.Nodes.Max(node => node.X) / Indent;
        var room = available - Math.Min(CardWidth, MinimumCardWidth);
        var indent = steps > 0 && steps * Indent > room ? Math.Max(MinimumIndent, room / steps) : Indent;
        var placed = indent < Indent ? Lay(widths, available, indent, railsOnly: true) : layout;
        var fitted = widths.Select((width, index) => index < placed.Nodes.Count ? Math.Max(1, Math.Min(width, available - placed.Nodes[index].X)) : width).ToList();
        layout = fitted.SequenceEqual(widths) && indent >= Indent ? layout : Lay(fitted, available, indent, railsOnly: true);
    }

    private AgentTreeGraphLayout? Best(List<double> widths, double available, AgentTreeGraphLayout wide)
    {
        foreach (var shape in new[] { AgentTreeShape.Tree, AgentTreeShape.Stacked, AgentTreeShape.Comb })
        {
            if (wide.Shape <= shape && wide.Shape != AgentTreeShape.Rail)
            {
                return wide;
            }

            var narrowed = Narrower.Select(share => Lay(Narrowed(widths, share), available, Indent, railsOnly: false)).FirstOrDefault(tried => tried.Shape == shape);

            if (narrowed is not null)
            {
                return narrowed;
            }
        }

        return null;
    }

    private AgentTreeGraphLayout Lay(List<double> widths, double available, double indent, bool railsOnly)
    {
        for (var index = 0; index < Children.Count; index++)
        {
            Children[index].Measure(widths[index] > 0 ? new Size(widths[index], double.PositiveInfinity) : Size.Infinity);
        }

        return AgentTreeLayout.Arrange(
            [.. Children.Select((child, index) => new TreeNodeSpec(Graph.Read(child, Graph.DepthProperty), new GraphSize(widths[index] > 0 ? widths[index] : child.DesiredSize.Width, child.DesiredSize.Height)))],
            new AgentTreeSpacing(Gap, indent, Drop),
            available,
            railsOnly);
    }

    private List<double> Narrowed(List<double> widths, double share) =>
        [.. widths.Select((width, index) => width > 0 && Graph.Read(Children[index], Graph.DepthProperty) > 0 ? Math.Max(Math.Min(width, ReadableCardWidth), width * share) : width)];

    private double CardWidthOf(Control child) =>
        Graph.Read(child, Graph.DepthProperty) == 0 && RootWidth > 0 ? RootWidth : CardWidth;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var keys = new object[Math.Min(Children.Count, layout.Nodes.Count)];

        for (var index = 0; index < keys.Length; index++)
        {
            var bounds = layout.Nodes[index];
            Children[index].Arrange(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            keys[index] = KeyOf(Children[index]);
        }

        var animate = !Motion.GetIsReduced(this) && TopLevel.GetTopLevel(this) is not null && Math.Abs(finalSize.Width - arrangedWidth) < 0.5;
        arrangedWidth = finalSize.Width;
        motion.Retarget(keys, layout.Nodes, layout.Parents, animate);
        Show();
        edges.Fit(finalSize);
        joints.Fit(finalSize);

        if (motion.IsActive)
        {
            Request();
        }

        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (Array.IndexOf(Strokes, change.Property) >= 0)
        {
            strokes.Reset();
        }
        else if (change.Property == Motion.IsReducedProperty)
        {
            Settle();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Settle();
    }

    private static object KeyOf(Control child) => child is ContentPresenter { Content: { } content } ? content : child;

    private void Settle()
    {
        motion.Snap();
        strokes.Rest();
        Show();
        EdgeLayer.Refresh(this);
    }

    private void Request()
    {
        if (!framing && TopLevel.GetTopLevel(this) is { } top)
        {
            framing = true;
            top.RequestAnimationFrame(frame);
        }
    }

    private void Frame(TimeSpan now)
    {
        framing = false;

        if (TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        if (motion.IsActive)
        {
            motion.Step(now);
            Show();
            EdgeLayer.Refresh(this);
        }

        var looping = loops && IsEffectivelyVisible && !Motion.GetIsReduced(this);

        if (looping && strokes.Step(now))
        {
            edges.InvalidateVisual();
        }

        if (motion.IsActive || looping)
        {
            Request();
        }
    }

    private void Show()
    {
        var shown = new GraphRect[Math.Min(Children.Count, layout.Nodes.Count)];
        var grown = new double[shown.Length];

        for (var index = 0; index < shown.Length; index++)
        {
            var track = motion.Of(KeyOf(Children[index]), layout.Nodes[index]);
            Place(Children[index], track);
            (shown[index], grown[index]) = (track.Shown, track.Grown);
        }

        routes = AgentTreeLayout.Connect(layout.Shape, shown, layout.Parents, layout.Spacing, grown);
        shapes = [.. routes.Edges.Select(edge => edge.Points.Count > 2 ? Line(edge.Points) : null)];
        ghosts.Clear();

        foreach (var ghost in motion.Ghosts)
        {
            var route = ghost.IsAnchored ? AgentTreeLayout.Partial(Reach(ghost.Toward, ghost.Rect), ghost.Remaining) : [];
            ghosts.Add((ghost, route, route.Count > 1 ? Line(route) : null));
        }
    }

    private IReadOnlyList<GraphPoint> Reach(GraphPoint from, GraphRect to)
    {
        if (layout.Shape == AgentTreeShape.Rail)
        {
            return [from, new GraphPoint(from.X, to.Center.Y), new GraphPoint(to.X, to.Center.Y)];
        }

        var bar = to.Y - (layout.Spacing.Drop / 2);

        return [from, new GraphPoint(from.X, bar), new GraphPoint(to.Center.X, bar), new GraphPoint(to.Center.X, to.Y)];
    }

    private static void Place(Control child, AgentTreeMotion.Track track)
    {
        var (x, y) = (track.Shown.X - track.Target.X, track.Shown.Y - track.Target.Y + track.Lift);

        if (child.RenderTransform is TranslateTransform shift && child.IsSet(RenderTransformProperty))
        {
            (shift.X, shift.Y) = (x, y);
        }
        else if (child.RenderTransform is null && (x != 0 || y != 0))
        {
            child.RenderTransform = new TranslateTransform(x, y);
        }

        if (track.IsFading)
        {
            child.Opacity = track.Opacity;
        }
        else if (child.IsSet(OpacityProperty) && child.Opacity < 1)
        {
            child.ClearValue(OpacityProperty);
        }
    }

    private void DrawEdges(DrawingContext context, EdgePens pens)
    {
        var (flowing, waiting) = (false, false);

        foreach (var (ghost, _, shape) in ghosts)
        {
            using (context.PushOpacity(ghost.Remaining))
            {
                var rect = ghost.Rect;
                context.DrawRectangle(JointBrush, strokes.For(this, EdgeKind.Quiet), new Rect(rect.X, rect.Y, rect.Width, rect.Height), Corner * 2, Corner * 2);

                if (shape is not null)
                {
                    context.DrawGeometry(null, strokes.For(this, EdgeKind.Quiet), shape);
                }
            }
        }

        for (var index = 0; index < routes.Edges.Count; index++)
        {
            var edge = routes.Edges[index];
            var kind = edge.Node >= 0 && edge.Node < Children.Count ? Graph.Read(Children[edge.Node], Graph.EdgeProperty) : EdgeKind.Quiet;
            (flowing, waiting) = (flowing || kind == EdgeKind.Flowing, waiting || kind == EdgeKind.Attention);

            using (context.PushOpacity(kind == EdgeKind.Attention ? strokes.Breathing : 1))
            {
                Stroke(context, strokes.For(this, kind), edge.Points, shapes[index]);
            }
        }

        loops = flowing || waiting;

        if (loops && !Motion.GetIsReduced(this))
        {
            Request();
        }
    }

    private static void Stroke(DrawingContext context, IPen pen, IReadOnlyList<GraphPoint> points, StreamGeometry? shape)
    {
        if (shape is not null)
        {
            context.DrawGeometry(null, pen, shape);
        }
        else
        {
            context.DrawLine(pen, At(points[0]), At(points[^1]));
        }
    }

    private void DrawJoints(DrawingContext context, EdgePens pens)
    {
        if (routes.Joints.Count == 0)
        {
            return;
        }

        var pen = strokes.For(this, EdgeKind.Quiet);

        for (var index = 0; index < routes.Joints.Count; index++)
        {
            var joint = routes.Joints[index];
            context.DrawEllipse(JointBrush, pen, new Point(joint.X, joint.Y), JointRadius, JointRadius);
        }
    }

    private static StreamGeometry Line(IReadOnlyList<GraphPoint> points)
    {
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(At(points[0]), false);

            for (var index = 1; index < points.Count - 1; index++)
            {
                var (before, corner, after) = (At(points[index - 1]), At(points[index]), At(points[index + 1]));
                var (into, onward) = (Between(before, corner), Between(corner, after));
                var radius = Math.Min(Corner, Math.Min(into.Length, onward.Length) / 2);
                stream.LineTo(corner - (Unit(into) * radius));
                stream.QuadraticBezierTo(corner, corner + (Unit(onward) * radius));
            }

            stream.LineTo(At(points[^1]));
            stream.EndFigure(false);
        }

        return geometry;
    }

    private static Point At(GraphPoint point) => new(point.X, point.Y);

    private static Vector Between(Point from, Point to) => new(to.X - from.X, to.Y - from.Y);

    private static Vector Unit(Vector vector) => vector.Length > 0 ? vector / vector.Length : default;
}
