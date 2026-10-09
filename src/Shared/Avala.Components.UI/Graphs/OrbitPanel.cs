using Avala.Components.Graphs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Graphs;

public sealed class OrbitPanel : Panel
{
    public static readonly StyledProperty<double> HubRadiusProperty =
        AvaloniaProperty.Register<OrbitPanel, double>(nameof(HubRadius), 64);

    private IReadOnlyList<GraphRect> placed = [];
    private IReadOnlyList<GraphSize> sizes = [];

    static OrbitPanel() => AffectsArrange<OrbitPanel>(HubRadiusProperty);

    public double HubRadius
    {
        get => GetValue(HubRadiusProperty);
        set => SetValue(HubRadiusProperty, value);
    }

    public IReadOnlyList<GraphSize> Sizes => sizes;

    public IReadOnlyList<GraphRect> Placed => placed;

    internal void Place(IReadOnlyList<GraphRect> rects)
    {
        if (!rects.SequenceEqual(placed))
        {
            placed = rects;
            InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        IReadOnlyList<GraphSize> measured = [.. Children.Select(child => new GraphSize(child.DesiredSize.Width, child.DesiredSize.Height))];

        if (!measured.SequenceEqual(sizes))
        {
            sizes = measured;
            this.FindAncestorOfType<HubGraphPanel>()?.InvalidateMeasure();
        }

        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rects = placed.Count == Children.Count ? placed : Alone();

        for (var index = 0; index < Children.Count; index++)
        {
            var rect = rects[index];
            Children[index].Arrange(new Rect(rect.X + (finalSize.Width / 2), rect.Y + (finalSize.Height / 2), rect.Width, rect.Height));
        }

        return finalSize;
    }

    private IReadOnlyList<GraphRect> Alone()
    {
        var layout = HubLayout.Arrange([new HubSpec(HubRadius, sizes)], new HubSpacing());

        if (layout.Hubs.Count == 0)
        {
            return [];
        }

        var hub = layout.Hubs[0];

        return [.. hub.Satellites.Select(rect => rect with { X = rect.X - hub.Center.X, Y = rect.Y - hub.Center.Y })];
    }
}
