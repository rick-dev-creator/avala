using Avala.Components.Graphs;
using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Avala.Components.UI.Graphs;

public sealed class RelayTrack : Control
{
    public static readonly StyledProperty<RelayCourse> CourseProperty =
        AvaloniaProperty.Register<RelayTrack, RelayCourse>(nameof(Course), RelayCourse.Empty);

    public static readonly StyledProperty<double> LaneHeightProperty =
        AvaloniaProperty.Register<RelayTrack, double>(nameof(LaneHeight), 64);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<RelayTrack, IBrush>(nameof(TrackBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> RouteBrushProperty =
        AvaloniaProperty.Register<RelayTrack, IBrush>(nameof(RouteBrush), Brushes.MediumPurple);

    public static readonly StyledProperty<IBrush> WaitBrushProperty =
        AvaloniaProperty.Register<RelayTrack, IBrush>(nameof(WaitBrush), Brushes.Goldenrod);

    public static readonly StyledProperty<IBrush> CapBrushProperty =
        AvaloniaProperty.Register<RelayTrack, IBrush>(nameof(CapBrush), Brushes.IndianRed);

    public static readonly StyledProperty<IBrush> SurfaceBrushProperty =
        AvaloniaProperty.Register<RelayTrack, IBrush>(nameof(SurfaceBrush), Brushes.Black);

    public static readonly TimeSpan Reveal = TimeSpan.FromMilliseconds(1100);

    public static readonly TimeSpan Pulse = TimeSpan.FromMilliseconds(1600);

    private const double Inset = 18;

    private const double Bend = 56;

    private const double Thickness = 2.6;

    private const double Dash = 6;

    private const double Gap = 7;

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private RelayRoute route = RelayRoute.Empty;
    private DispatcherTimer? timer;
    private double shownFrom = 1;
    private TimeSpan revealedAt;

    static RelayTrack()
    {
        AffectsMeasure<RelayTrack>(CourseProperty, LaneHeightProperty);
        AffectsRender<RelayTrack>(TrackBrushProperty, RouteBrushProperty, WaitBrushProperty, CapBrushProperty, SurfaceBrushProperty);
    }

    public RelayCourse Course
    {
        get => GetValue(CourseProperty);
        set => SetValue(CourseProperty, value);
    }

    public double LaneHeight
    {
        get => GetValue(LaneHeightProperty);
        set => SetValue(LaneHeightProperty, value);
    }

    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush RouteBrush
    {
        get => GetValue(RouteBrushProperty);
        set => SetValue(RouteBrushProperty, value);
    }

    public IBrush WaitBrush
    {
        get => GetValue(WaitBrushProperty);
        set => SetValue(WaitBrushProperty, value);
    }

    public IBrush CapBrush
    {
        get => GetValue(CapBrushProperty);
        set => SetValue(CapBrushProperty, value);
    }

    public IBrush SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public RelayRoute Route => route;

    public bool IsMoving => timer?.IsEnabled == true;

    public double Revealed => Moving ? Math.Clamp(shownFrom + ((1 - shownFrom) * Ease(Progress)), 0, 1) : 1;

    private bool Moving => !Motion.GetIsReduced(this) && TopLevel.GetTopLevel(this) is not null && IsEffectivelyVisible;

    private double Progress => Math.Clamp((clock.Elapsed - revealedAt) / Reveal, 0, 1);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width, Course.Lanes * LaneHeight);

    protected override Size ArrangeOverride(Size finalSize)
    {
        route = RelayLayout.Route(Course, new RelaySpacing(LaneHeight, Inset, Bend), finalSize.Width);

        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CourseProperty && change.OldValue is RelayCourse before && change.NewValue is RelayCourse after && !before.Equivalent(after))
        {
            var length = route.Length;
            shownFrom = before.Lanes == 0 || length <= 0 ? 0 : Math.Min(Revealed, 1) * Math.Min(1, length / Math.Max(1, RelayLayout.Route(after, new RelaySpacing(LaneHeight, Inset, Bend), Bounds.Width).Length));
            revealedAt = clock.Elapsed;
            Animate();
        }
        else if (change.Property == IsVisibleProperty || change.Property == Motion.IsReducedProperty)
        {
            Animate();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Animate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        timer?.Stop();
    }

    public override void Render(DrawingContext context)
    {
        DrawLanes(context);

        var revealed = Revealed;
        var length = route.Length * revealed;
        var phase = Moving ? clock.Elapsed.Ticks % Pulse.Ticks / (double)Pulse.Ticks : 0;
        var glow = new Pen(RouteBrush, 10, lineCap: PenLineCap.Round);
        var left = length;

        foreach (var piece in route.Pieces)
        {
            if (left <= 0)
            {
                break;
            }

            var part = Math.Min(1, left / Math.Max(piece.Length, 0.001));
            left -= piece.Length;
            var geometry = Shape(piece, part);

            using (context.PushOpacity(0.12))
            {
                context.DrawGeometry(null, glow, geometry);
            }

            context.DrawGeometry(null, PenFor(piece.Stretch, phase), geometry);
        }

        DrawMarkers(context, length);
        DrawBaton(context, revealed, phase);
    }

    private void DrawLanes(DrawingContext context)
    {
        var pen = new Pen(TrackBrush, 1, new DashStyle([2, 5], 0));

        foreach (var lane in route.Lanes)
        {
            context.DrawLine(pen, new Point(0, lane), new Point(Bounds.Width, lane));
        }
    }

    private Pen PenFor(RelayStretchKind kind, double phase) => kind switch
    {
        RelayStretchKind.Waiting => new Pen(WaitBrush, Thickness, new DashStyle([1.2, 2.4], -phase * 3.6), PenLineCap.Round),
        RelayStretchKind.Running => new Pen(RouteBrush, Thickness + 0.6, new DashStyle([Dash / Thickness, Gap / Thickness], -phase * (Dash + Gap) / Thickness * 2), PenLineCap.Round),
        _ => new Pen(RouteBrush, Thickness, lineCap: PenLineCap.Round),
    };

    private void DrawMarkers(DrawingContext context, double reached)
    {
        foreach (var marker in route.Markers)
        {
            if (Distance(marker.At) > reached + 0.5)
            {
                continue;
            }

            var at = new Point(marker.At.X, marker.At.Y);

            switch (marker.Kind)
            {
                case RelayStopKind.HandedOff:
                    context.DrawGeometry(SurfaceBrush, new Pen(RouteBrush, 2), Diamond(at, 7));
                    break;
                case RelayStopKind.Capped:
                    context.DrawRectangle(CapBrush, null, new Rect(at.X - 4.5, at.Y - 4.5, 9, 9));
                    break;
                case RelayStopKind.Compacting:
                    context.DrawGeometry(SurfaceBrush, new Pen(WaitBrush, 1.8), Diamond(at, 5.5));
                    break;
                case RelayStopKind.Resumed:
                    context.DrawGeometry(RouteBrush, null, Diamond(at, 5));
                    break;
                default:
                    context.DrawGeometry(WaitBrush, null, Diamond(at, 5.5));
                    break;
            }
        }
    }

    private double Distance(GraphPoint point)
    {
        var travelled = 0d;

        foreach (var piece in route.Pieces)
        {
            if (piece.Kind == RelayPieceKind.Straight && Math.Abs(piece.From.Y - point.Y) < 0.5 && point.X >= piece.From.X - 0.5 && point.X <= piece.To.X + 0.5)
            {
                return travelled + (point.X - piece.From.X);
            }

            travelled += piece.Length;
        }

        return travelled;
    }

    private void DrawBaton(DrawingContext context, double revealed, double phase)
    {
        if (!Course.IsLive && revealed >= 1)
        {
            return;
        }

        foreach (var point in route.Along(revealed).Match<GraphPoint[]>(found => [found], () => []))
        {
            var at = new Point(point.X, point.Y);
            var waiting = Course.Stretches is [.., { Kind: RelayStretchKind.Waiting }];
            var brush = waiting ? WaitBrush : RouteBrush;
            var halo = 8 + (8 * phase);

            using (context.PushOpacity(0.35 * (1 - phase)))
            {
                context.DrawGeometry(null, new Pen(brush, 1.5), Diamond(at, halo + 2));
            }

            using (context.PushOpacity(0.2))
            {
                context.DrawGeometry(brush, null, Diamond(at, 12));
            }

            context.DrawGeometry(brush, new Pen(SurfaceBrush, 2), Diamond(at, 7.5));
        }
    }

    private static StreamGeometry Shape(RelayPiece piece, double part)
    {
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(piece.From.X, piece.From.Y), false);

            if (piece.Kind == RelayPieceKind.Straight || part >= 1)
            {
                var end = piece.At(part);

                if (piece.Kind == RelayPieceKind.Straight)
                {
                    stream.LineTo(new Point(end.X, end.Y));
                }
                else
                {
                    var (first, second) = piece.Handles();
                    stream.CubicBezierTo(new Point(first.X, first.Y), new Point(second.X, second.Y), new Point(piece.To.X, piece.To.Y));
                }
            }
            else
            {
                for (var step = 1; step <= 24; step++)
                {
                    var point = piece.At(part * step / 24);
                    stream.LineTo(new Point(point.X, point.Y));
                }
            }

            stream.EndFigure(false);
        }

        return geometry;
    }

    private static StreamGeometry Diamond(Point at, double radius)
    {
        var geometry = new StreamGeometry();

        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(at.X, at.Y - radius), true);
            stream.LineTo(new Point(at.X + radius, at.Y));
            stream.LineTo(new Point(at.X, at.Y + radius));
            stream.LineTo(new Point(at.X - radius, at.Y));
            stream.EndFigure(true);
        }

        return geometry;
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    private void Animate()
    {
        if (Moving && (Course.IsLive || Progress < 1))
        {
            timer ??= new DispatcherTimer(Frame, DispatcherPriority.Render, (_, _) => Tick());
            timer.Start();
        }
        else
        {
            timer?.Stop();
            InvalidateVisual();
        }
    }

    private void Tick()
    {
        InvalidateVisual();

        if (!Moving || (!Course.IsLive && Progress >= 1))
        {
            timer?.Stop();
        }
    }
}
