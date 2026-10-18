using Avala.Components.UI.Copying;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Avala.Components.UI.Zooming;

public sealed class ZoomSurface : Border
{
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<ZoomSurface, double>(nameof(Scale), 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsFittedProperty =
        AvaloniaProperty.Register<ZoomSurface, bool>(nameof(IsFitted), true, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ZoomSurface, double>(nameof(Minimum), 0.25);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ZoomSurface, double>(nameof(Maximum), 4);

    public static readonly StyledProperty<Thickness> FitMarginProperty =
        AvaloniaProperty.Register<ZoomSurface, Thickness>(nameof(FitMargin));

    public static readonly StyledProperty<bool> WheelZoomsProperty =
        AvaloniaProperty.Register<ZoomSurface, bool>(nameof(WheelZooms), true);

    public const double Kept = 48;

    public const double WheelStep = 1.2;

    private const double LargestFit = 1.25;

    private readonly MatrixTransform transform = new();
    private Size viewport;
    private Vector offset;
    private double applied = 1;
    private double floor = double.MaxValue;
    private double? pinched;
    private (IPointer Pointer, Point At)? grabbed;
    private bool moved;
    private bool placed;

    static ZoomSurface()
    {
        AffectsArrange<ZoomSurface>(IsFittedProperty, FitMarginProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ZoomSurface>(true);
        BackgroundProperty.OverrideDefaultValue<ZoomSurface>(Brushes.Transparent);
    }

    public ZoomSurface()
    {
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, Pinching);
        AddHandler(PinchEndedEvent, (_, _) => pinched = null);
        AddHandler(PointerTouchPadGestureMagnifyEvent, Magnifying);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public bool IsFitted
    {
        get => GetValue(IsFittedProperty);
        set => SetValue(IsFittedProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Thickness FitMargin
    {
        get => GetValue(FitMarginProperty);
        set => SetValue(FitMarginProperty, value);
    }

    public bool WheelZooms
    {
        get => GetValue(WheelZoomsProperty);
        set => SetValue(WheelZoomsProperty, value);
    }

    public Rect Shown => Child is { } child ? new Rect(offset.X, offset.Y, child.DesiredSize.Width * applied, child.DesiredSize.Height * applied) : default;

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(Size.Infinity);
        var content = Child?.DesiredSize ?? default;

        return new Size(double.IsInfinity(availableSize.Width) ? content.Width : availableSize.Width, double.IsInfinity(availableSize.Height) ? content.Height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        viewport = finalSize;

        if (Child is not { } child)
        {
            return finalSize;
        }

        child.RenderTransformOrigin = RelativePoint.TopLeft;
        child.RenderTransform = transform;
        child.Arrange(new Rect(child.DesiredSize));

        if (IsFitted)
        {
            Fit(child.DesiredSize);
        }
        else if (!placed)
        {
            applied = Scale;
            offset = new Vector((finalSize.Width - (child.DesiredSize.Width * applied)) / 2, (finalSize.Height - (child.DesiredSize.Height * applied)) / 2);
        }

        placed = finalSize.Width > 0 && finalSize.Height > 0;
        Place();

        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ScaleProperty && placed && !IsFitted && Math.Abs(Scale - applied) > 1e-9)
        {
            ZoomAt(new Point(viewport.Width / 2, viewport.Height / 2), Scale);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.Delta.Y != 0 && (WheelZooms || (e.KeyModifiers & (Hotkeys.Command | KeyModifiers.Control)) != 0))
        {
            ZoomAt(e.GetPosition(this), applied * Math.Pow(WheelStep, e.Delta.Y));
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (grabbed is null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            grabbed = (e.Pointer, e.GetPosition(this));
            moved = false;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (grabbed is { } hold && hold.Pointer == e.Pointer && pinched is null)
        {
            var at = e.GetPosition(this);
            moved |= Pan(at - hold.At);
            grabbed = (hold.Pointer, at);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (grabbed is { } hold && hold.Pointer == e.Pointer)
        {
            e.Handled = moved;
            grabbed = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (grabbed is { } hold && hold.Pointer == e.Pointer)
        {
            grabbed = null;
        }
    }

    private void Pinching(object? sender, PinchEventArgs e)
    {
        pinched ??= applied;
        grabbed = null;
        ZoomAt(e.ScaleOrigin, pinched.Value * e.Scale);
        e.Handled = true;
    }

    private void Magnifying(object? sender, PointerDeltaEventArgs e)
    {
        ZoomAt(e.GetPosition(this), applied * (1 + e.Delta.X));
        e.Handled = true;
    }

    private void ZoomAt(Point point, double scale)
    {
        var bounded = Math.Clamp(scale, Math.Min(Math.Min(Minimum, floor), applied), Math.Max(Maximum, applied));

        if (Math.Abs(bounded - applied) < 1e-9)
        {
            return;
        }

        var anchor = (point - offset) / applied;
        offset = (Vector)point - (anchor * bounded);
        applied = bounded;
        SetCurrentValue(IsFittedProperty, false);
        SetCurrentValue(ScaleProperty, bounded);
        Place();
    }

    private bool Pan(Vector delta)
    {
        if (Child is null || delta == default)
        {
            return false;
        }

        var before = offset;
        offset += delta;
        Place();

        if (offset == before)
        {
            return false;
        }

        SetCurrentValue(IsFittedProperty, false);

        return true;
    }

    private void Fit(Size content)
    {
        var room = new Rect(viewport).Deflate(FitMargin);

        if (content.Width <= 0 || content.Height <= 0 || room.Width <= 0 || room.Height <= 0)
        {
            return;
        }

        applied = Math.Min(LargestFit, Math.Min(room.Width / content.Width, room.Height / content.Height));
        floor = applied;
        offset = new Vector(room.X + ((room.Width - (content.Width * applied)) / 2), room.Y + ((room.Height - (content.Height * applied)) / 2));
        SetCurrentValue(ScaleProperty, applied);
    }

    private void Place()
    {
        if (Child is { } child)
        {
            offset = new Vector(Bound(offset.X, child.DesiredSize.Width * applied, viewport.Width), Bound(offset.Y, child.DesiredSize.Height * applied, viewport.Height));
        }

        transform.Matrix = new Matrix(applied, 0, 0, applied, offset.X, offset.Y);
    }

    private static double Bound(double at, double size, double room)
    {
        var kept = Math.Min(Kept, size);

        return room <= 0 ? at : Math.Clamp(at, kept - size, Math.Max(room - kept, kept - size));
    }
}
