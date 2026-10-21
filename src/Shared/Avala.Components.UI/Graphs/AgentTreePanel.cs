using Avala.Components.Graphs;
using Avalonia;
using Avalonia.Controls;
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

    private static readonly double[] Narrower = [0.9, 0.8, 0.72];

    private static readonly int Kinds = Enum.GetValues<EdgeKind>().Length;

    private AgentTreeGraphLayout layout = AgentTreeGraphLayout.Empty;
    private readonly EdgeLayer edges;
    private readonly EdgeLayer joints;

    static AgentTreePanel()
    {
        AffectsMeasure<AgentTreePanel>(GapProperty, IndentProperty, DropProperty, CardWidthProperty, RootWidthProperty);
        JointBrushProperty.Changed.AddClassHandler<AgentTreePanel>((panel, _) => EdgeLayer.Refresh(panel));
    }

    public AgentTreePanel()
    {
        edges = new EdgeLayer(this, DrawEdges);
        joints = new EdgeLayer(this, DrawJoints) { ZIndex = int.MaxValue };
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

    protected override Size MeasureOverride(Size availableSize)
    {
        Fit(availableSize.Width);

        return new Size(layout.Size.Width, layout.Size.Height);
    }

    private void Fit(double available)
    {
        var widths = Children.Select(CardWidthOf).ToList();
        layout = Lay(widths, available, Indent, railsOnly: false);

        if (layout.Shape != AgentTreeShape.Rail || !double.IsFinite(available) || CardWidth <= 0 || layout.Nodes.Count == 0)
        {
            return;
        }

        foreach (var share in Narrower)
        {
            var narrower = widths.Select(width => width > 0 ? Math.Max(Math.Min(width, MinimumCardWidth), width * share) : width).ToList();
            var tried = Lay(narrower, available, Indent, railsOnly: false);

            if (tried.Shape == AgentTreeShape.Comb)
            {
                layout = tried;

                return;
            }
        }

        layout = Lay(widths, available, Indent, railsOnly: true);

        var steps = layout.Nodes.Max(node => node.X) / Indent;
        var room = available - Math.Min(CardWidth, MinimumCardWidth);
        var indent = steps > 0 && steps * Indent > room ? Math.Max(MinimumIndent, room / steps) : Indent;
        var placed = indent < Indent ? Lay(widths, available, indent, railsOnly: true) : layout;
        var fitted = widths.Select((width, index) => index < placed.Nodes.Count ? Math.Max(1, Math.Min(width, available - placed.Nodes[index].X)) : width).ToList();
        layout = fitted.SequenceEqual(widths) && indent >= Indent ? layout : Lay(fitted, available, indent, railsOnly: true);
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

    private double CardWidthOf(Control child) =>
        Graph.Read(child, Graph.DepthProperty) == 0 && RootWidth > 0 ? RootWidth : CardWidth;

    protected override Size ArrangeOverride(Size finalSize)
    {

        for (var index = 0; index < Children.Count && index < layout.Nodes.Count; index++)
        {
            var bounds = layout.Nodes[index];
            Children[index].Arrange(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        edges.Fit(finalSize);
        joints.Fit(finalSize);

        return finalSize;
    }

    private void DrawEdges(DrawingContext context, EdgePens pens)
    {
        var made = new IPen?[Kinds];

        foreach (var edge in layout.Edges)
        {
            var kind = edge.Node >= 0 && edge.Node < Children.Count ? Graph.Read(Children[edge.Node], Graph.EdgeProperty) : EdgeKind.Quiet;
            var pen = made[(int)kind] ??= pens.For(kind);
            context.DrawGeometry(null, pen, Line(edge.Points));
        }
    }

    private void DrawJoints(DrawingContext context, EdgePens pens)
    {
        if (layout.Joints.Count == 0)
        {
            return;
        }

        var pen = pens.For(EdgeKind.Quiet);

        foreach (var joint in layout.Joints)
        {
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
