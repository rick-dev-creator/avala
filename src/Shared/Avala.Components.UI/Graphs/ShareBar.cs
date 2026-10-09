using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Graphs;

public sealed class ShareBar : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<ShareBar, double>(nameof(Gap), 2);

    static ShareBar() => AffectsArrange<ShareBar>(GapProperty);

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var weights = Children.Select(child => Math.Max(0, Graph.Read(child, Graph.WeightProperty))).ToArray();
        var shown = weights.Count(weight => weight > 0);
        var total = weights.Sum();
        var room = Math.Max(0, finalSize.Width - (Gap * Math.Max(0, shown - 1)));
        var x = 0.0;

        for (var index = 0; index < Children.Count; index++)
        {
            var width = total > 0 ? room * weights[index] / total : 0;
            Children[index].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + (width > 0 ? Gap : 0);
        }

        return finalSize;
    }
}
