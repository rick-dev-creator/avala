using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI;

public sealed class FoldHeaderPanel : Panel
{
    private (double Header, double Fact) widths;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children is not [var header, var fact])
        {
            return base.MeasureOverride(availableSize);
        }

        header.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        fact.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        widths = Shared(header.DesiredSize.Width, fact.DesiredSize.Width, availableSize.Width);
        header.Measure(new Size(widths.Header, availableSize.Height));
        fact.Measure(new Size(widths.Fact, availableSize.Height));

        return new Size(widths.Header + widths.Fact, Math.Max(header.DesiredSize.Height, fact.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children is not [var header, var fact])
        {
            return base.ArrangeOverride(finalSize);
        }

        var (headerWidth, factWidth) = widths.Header + widths.Fact > finalSize.Width
            ? Shared(widths.Header, widths.Fact, finalSize.Width)
            : widths;
        header.Arrange(new Rect(0, 0, Math.Max(headerWidth, finalSize.Width - factWidth), finalSize.Height));
        fact.Arrange(new Rect(finalSize.Width - factWidth, 0, factWidth, finalSize.Height));

        return finalSize;
    }

    private static (double Header, double Fact) Shared(double header, double fact, double available)
    {
        if (double.IsInfinity(available) || header + fact <= available)
        {
            return (header, fact);
        }

        var half = available / 2;

        return header <= half ? (header, available - header)
            : fact <= half ? (available - fact, fact)
            : (half, half);
    }
}
