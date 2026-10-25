using Avalonia;
using Avalonia.Controls;

namespace Avala.Preview.UI;

internal sealed class DeviceBox : Panel
{
    public static readonly StyledProperty<double> OuterWidthProperty =
        AvaloniaProperty.Register<DeviceBox, double>(nameof(OuterWidth), double.PositiveInfinity);

    public static readonly StyledProperty<double> OuterHeightProperty =
        AvaloniaProperty.Register<DeviceBox, double>(nameof(OuterHeight), double.PositiveInfinity);

    static DeviceBox() => AffectsMeasure<DeviceBox>(OuterWidthProperty, OuterHeightProperty);

    public double OuterWidth
    {
        get => GetValue(OuterWidthProperty);
        set => SetValue(OuterWidthProperty, value);
    }

    public double OuterHeight
    {
        get => GetValue(OuterHeightProperty);
        set => SetValue(OuterHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (width, height) = (0d, 0d);

        foreach (var child in Children)
        {
            child.Measure(Fit(child, availableSize));
            (width, height) = (Math.Max(width, child.DesiredSize.Width), Math.Max(height, child.DesiredSize.Height));
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var size = Fit(child, finalSize);
            child.Arrange(new Rect((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
        }

        return finalSize;
    }

    private Size Fit(Control child, Size room)
    {
        if (!double.IsFinite(OuterWidth) || !double.IsFinite(OuterHeight) || OuterWidth <= 0 || OuterHeight <= 0)
        {
            return room;
        }

        var margin = child.Margin;
        var scale = Math.Clamp(Math.Min((room.Width - margin.Left - margin.Right) / OuterWidth, (room.Height - margin.Top - margin.Bottom) / OuterHeight), 0, 1);

        return new Size((OuterWidth * scale) + margin.Left + margin.Right, (OuterHeight * scale) + margin.Top + margin.Bottom);
    }
}
