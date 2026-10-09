using Avala.Components.Graphs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Graphs;

public sealed class TreeGraphPanel : Panel
{
    public static readonly StyledProperty<double> LeadProperty =
        AvaloniaProperty.Register<TreeGraphPanel, double>(nameof(Lead), 240);

    private TreeGraphLayout layout = TreeGraphLayout.Empty;
    private readonly EdgeLayer edges;

    static TreeGraphPanel() => AffectsMeasure<TreeGraphPanel>(LeadProperty);

    public double Lead
    {
        get => GetValue(LeadProperty);
        set => SetValue(LeadProperty, value);
    }

    public TreeGraphPanel()
    {
        edges = new EdgeLayer(this, Draw);
        VisualChildren.Add(edges);
    }

    public TreeGraphLayout Layout => layout;

    private void Draw(DrawingContext context, EdgePens pens)
    {
        for (var index = 0; index < layout.Nodes.Count && index < Children.Count; index++)
        {
            var node = layout.Nodes[index];
            var from = node.Parent < 0
                ? new Point(layout.Root.X, layout.Root.Y)
                : new Point(layout.Nodes[node.Parent].Bounds.Right, layout.Nodes[node.Parent].Bounds.Center.Y);
            var to = new Point(node.Bounds.X, node.Bounds.Center.Y);
            context.DrawGeometry(null, pens.For(Graph.Read(Children[index], Graph.EdgeProperty)), Curve(from, to));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        layout = TreeLayout.Arrange(
            [.. Children.Select(child => new TreeNodeSpec(Graph.Read(child, Graph.DepthProperty), new GraphSize(child.DesiredSize.Width, child.DesiredSize.Height)))],
            new TreeSpacing(Lead));

        return new Size(layout.Size.Width, layout.Size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < Children.Count && index < layout.Nodes.Count; index++)
        {
            var bounds = layout.Nodes[index].Bounds;
            Children[index].Arrange(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        edges.Fit(finalSize);

        return finalSize;
    }

    private static StreamGeometry Curve(Point from, Point to)
    {
        var middle = (from.X + to.X) / 2;
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(from, false);
            stream.CubicBezierTo(new Point(middle, from.Y), new Point(middle, to.Y), to);
            stream.EndFigure(false);
        }

        return geometry;
    }
}
