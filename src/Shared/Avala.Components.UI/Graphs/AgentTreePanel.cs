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

    public static readonly StyledProperty<IBrush> JointBrushProperty =
        AvaloniaProperty.Register<AgentTreePanel, IBrush>(nameof(JointBrush), Brushes.Transparent);

    private const double JointRadius = 3.5;

    private const double Corner = 6;

    private static readonly int Kinds = Enum.GetValues<EdgeKind>().Length;

    private AgentTreeGraphLayout layout = AgentTreeGraphLayout.Empty;
    private readonly EdgeLayer edges;
    private readonly EdgeLayer joints;

    static AgentTreePanel()
    {
        AffectsMeasure<AgentTreePanel>(GapProperty, IndentProperty, DropProperty);
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

    public IBrush JointBrush
    {
        get => GetValue(JointBrushProperty);
        set => SetValue(JointBrushProperty, value);
    }

    public AgentTreeGraphLayout Layout => layout;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        layout = AgentTreeLayout.Arrange(
            [.. Children.Select(child => new TreeNodeSpec(Graph.Read(child, Graph.DepthProperty), new GraphSize(child.DesiredSize.Width, child.DesiredSize.Height)))],
            new AgentTreeSpacing(Gap, Indent, Drop),
            availableSize.Width);

        return new Size(layout.Size.Width, layout.Size.Height);
    }

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
