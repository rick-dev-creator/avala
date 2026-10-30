using Avalonia;
using Avalonia.Media;

namespace Avala.Components.UI.Theme;

public sealed class Chamfer
{
    public static readonly AttachedProperty<double> SizeProperty =
        AvaloniaProperty.RegisterAttached<Chamfer, Visual, double>("Size");

    static Chamfer() => SizeProperty.Changed.AddClassHandler<Visual>((visual, _) => Watch(visual));

    private Chamfer()
    {
    }

    public static double GetSize(Visual visual) => visual.GetValue(SizeProperty);

    public static void SetSize(Visual visual, double value) => visual.SetValue(SizeProperty, value);

    public static IReadOnlyList<Point> Corners(Size size, double cut) =>
        cut <= 0 || size.Width <= cut * 2 || size.Height <= cut * 2
            ? []
            : [new(0, 0), new(size.Width - cut, 0), new(size.Width, cut), new(size.Width, size.Height), new(cut, size.Height), new(0, size.Height - cut)];

    private static void Watch(Visual visual)
    {
        visual.PropertyChanged -= Resized;

        if (GetSize(visual) > 0)
        {
            visual.PropertyChanged += Resized;
        }

        Cut(visual);
    }

    private static void Resized(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.BoundsProperty && sender is Visual visual && change.GetOldValue<Rect>().Size != change.GetNewValue<Rect>().Size)
        {
            Cut(visual);
        }
    }

    private static void Cut(Visual visual) =>
        visual.OpacityMask = Corners(visual.Bounds.Size, GetSize(visual)) is { Count: > 0 } corners
            ? new DrawingBrush(new GeometryDrawing { Geometry = new PolylineGeometry(corners, isFilled: true), Brush = Brushes.Black })
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            }
            : null;
}
