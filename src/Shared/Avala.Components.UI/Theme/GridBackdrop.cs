using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Theme;

public sealed class GridBackdrop : Control
{
    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<GridBackdrop, IBrush>(nameof(LineBrush), Brushes.Transparent);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<GridBackdrop, double>(nameof(Spacing), 48);

    static GridBackdrop() => AffectsRender<GridBackdrop>(LineBrushProperty, SpacingProperty);

    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static IEnumerable<double> Lines(double length, double spacing) =>
        spacing <= 0 ? [] : Enumerable.Range(1, (int)Math.Floor(length / spacing)).Select(step => (step * spacing) - 0.5);

    public override void Render(DrawingContext context)
    {
        var brush = LineBrush;
        var size = Bounds.Size;

        foreach (var x in Lines(size.Width, Spacing))
        {
            context.FillRectangle(brush, new Rect(x, 0, 1, size.Height));
        }

        foreach (var y in Lines(size.Height, Spacing))
        {
            context.FillRectangle(brush, new Rect(0, y, size.Width, 1));
        }
    }
}
