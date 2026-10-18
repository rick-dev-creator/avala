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
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, availableSize.Height));
        }

        return Lay(availableSize.Width, (_, _) => { });
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Lay(finalSize.Width, (child, place) => child.Arrange(place));

        return finalSize;
    }

    private Size Lay(double width, Action<Control, Rect> place)
    {
        var (x, y, line, widest) = (0d, 0d, 0d, 0d);

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (size.Width <= 0)
            {
                place(child, new Rect(x, y, 0, 0));
                continue;
            }

            if (x > 0 && x + Spacing + size.Width > width)
            {
                (x, y, line) = (0, y + line + Spacing, 0);
            }

            var left = x > 0 ? x + Spacing : 0;
            place(child, new Rect(left, y, size.Width, Math.Max(size.Height, line)));
            x = left + size.Width;
            line = Math.Max(line, size.Height);
            widest = Math.Max(widest, x);
        }

        return new Size(widest, y + line);
    }
}
