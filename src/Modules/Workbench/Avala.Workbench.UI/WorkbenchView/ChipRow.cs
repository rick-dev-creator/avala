using Avalonia;
using Avalonia.Controls;

namespace Avala.Workbench.UI;

internal sealed class ChipRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ChipRow, double>(nameof(Spacing));

    static ChipRow() => AffectsMeasure<ChipRow>(SpacingProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        var shown = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));

            if (child.DesiredSize.Width > 0)
            {
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
                shown++;
            }
        }

        return new Size(width + (Math.Max(shown - 1, 0) * Spacing), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;

        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width > 0 ? width + Spacing : 0;
        }

        return finalSize;
    }
}
