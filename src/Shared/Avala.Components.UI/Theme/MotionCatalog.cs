using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Styling;

namespace Avala.Components.UI.Theme;

internal static class MotionCatalog
{
    private const int Endless = 100_000;

    private static readonly SplineEasing Snappy = new(0.34, 1.4, 0.64, 1);
    private static readonly SplineEasing Gentle = new(0.22, 1, 0.36, 1);
    private static readonly RelativePoint TopLeft = new(0, 0, RelativeUnit.Relative);
    private static readonly RelativePoint TopCenter = new(0.5, 0, RelativeUnit.Relative);

    private static readonly Move FadeIn = Enter(0.12, null, Frame(0, [Opacity(0)]), Frame(1, [Opacity(1)]));

    private static readonly Move ArriveSnappy = Enter(0.24, Snappy, Frame(0, [Y(10), Opacity(0)]), Frame(1, [Y(0), Opacity(1)]));

    private static readonly Move ArriveGentle = Enter(0.42, Gentle, Frame(0, [X(24), Opacity(0)]), Frame(1, [X(0), Opacity(1)]));

    private static readonly Move SheetIn = Enter(0.42, Gentle, Frame(0, [Y(16), .. Scale(0.985), Opacity(0)]), Frame(1, [Y(0), .. Scale(1), Opacity(1)]));

    private static readonly Move PopIn = Enter(0.38, Gentle, Frame(0, [Y(-6), .. Scale(0.97), Opacity(0)]), Frame(1, [Y(0), .. Scale(1), Opacity(1)])) with { Origin = TopLeft };

    private static readonly Move FoldIn = Enter(0.22, Snappy, Frame(0, [ScaleY(1.4), Opacity(0)]), Frame(1, [ScaleY(1), Opacity(1)])) with { Origin = TopCenter };

    private static readonly Move RiseIn = Enter(0.3, Snappy, Frame(0, [Y(8), Opacity(0)]), Frame(1, [Y(0), Opacity(1)]));

    private static readonly Move StampIn = Enter(0.46, Snappy, Frame(0, [.. Scale(1.2), Opacity(0)]), Frame(0.6, [.. Scale(0.97), Opacity(1)]), Frame(1, [.. Scale(1), Opacity(1)]));

    private static readonly Move ArriveScale = Enter(0.42, Gentle, Frame(0, [.. Scale(0.94), Opacity(0)]), Frame(1, [.. Scale(1), Opacity(1)]));

    private static readonly Move DrawIn = new(Animated(0.6, Gentle, TimeSpan.FromSeconds(0.3), Frame(0, [Dash(12)]), Frame(1, [Dash(0)])), false) { Dashes = [12, 12] };

    public static Move Reveal { get; } = Enter(0.18, Gentle, Frame(0, [Y(6), Opacity(0)]), Frame(1, [Y(0), Opacity(1)]));

    private static readonly Move PulseLoop = Repeat(2, new CubicEaseOut(), TimeSpan.Zero, Frame(0, [.. Scale(1), Opacity(0.7)]), Frame(0.8, [.. Scale(2.6), Opacity(0)]), Frame(1, [.. Scale(2.6), Opacity(0)]));

    private static readonly Move SpinLoop = Repeat(1, null, TimeSpan.Zero, Frame(0, [Angle(0)]), Frame(1, [Angle(360)]));

    private static readonly Move ThinkLoop = Think(TimeSpan.Zero);

    private static readonly Move ThinkSecondLoop = Think(TimeSpan.FromSeconds(0.15));

    private static readonly Move ThinkThirdLoop = Think(TimeSpan.FromSeconds(0.3));

    private static readonly Move ShimmerLoop = Repeat(2.2, null, TimeSpan.Zero, Frame(0, [X(-48)]), Frame(1, [X(96)]));

    private static readonly Move ShimmerTextLoop = Repeat(2.2, null, TimeSpan.Zero, Frame(0, [X(48)]), Frame(1, [X(-96)]));

    private static readonly Move CaretLoop = Repeat(1, null, TimeSpan.Zero, Frame(0, [Opacity(1)]), Frame(0.49, [Opacity(1)]), Frame(0.5, [Opacity(0)]), Frame(1, [Opacity(0)]));

    private static readonly Move DriftLoop = Repeat(4.8, new SineEaseInOut(), TimeSpan.Zero, Frame(0, [X(0), Y(0)]), Frame(0.5, [X(2), Y(-4)]), Frame(1, [X(0), Y(0)]));

    public static IReadOnlyDictionary<MotionEntrance, Move> Entrances { get; } = new Dictionary<MotionEntrance, Move>
    {
        [MotionEntrance.Fade] = FadeIn,
        [MotionEntrance.Arrive] = ArriveSnappy,
        [MotionEntrance.Gentle] = ArriveGentle,
        [MotionEntrance.Sheet] = SheetIn,
        [MotionEntrance.Pop] = PopIn,
        [MotionEntrance.Fold] = FoldIn,
        [MotionEntrance.Rise] = RiseIn,
        [MotionEntrance.Stamp] = StampIn,
        [MotionEntrance.Scale] = ArriveScale,
        [MotionEntrance.Draw] = DrawIn,
    };

    public static IReadOnlyDictionary<MotionLoop, Move> Loops { get; } = new Dictionary<MotionLoop, Move>
    {
        [MotionLoop.Pulse] = PulseLoop,
        [MotionLoop.Spin] = SpinLoop,
        [MotionLoop.Think] = ThinkLoop,
        [MotionLoop.ThinkSecond] = ThinkSecondLoop,
        [MotionLoop.ThinkThird] = ThinkThirdLoop,
        [MotionLoop.Shimmer] = ShimmerLoop,
        [MotionLoop.ShimmerText] = ShimmerTextLoop,
        [MotionLoop.Caret] = CaretLoop,
        [MotionLoop.Drift] = DriftLoop,
    };

    public static IReadOnlyDictionary<MotionLoop, Pose> Stills { get; } = new Dictionary<MotionLoop, Pose>
    {
        [MotionLoop.Think] = new(Visual.OpacityProperty, 0.6),
        [MotionLoop.ThinkSecond] = new(Visual.OpacityProperty, 0.6),
        [MotionLoop.ThinkThird] = new(Visual.OpacityProperty, 0.6),
        [MotionLoop.Shimmer] = new(Visual.OpacityProperty, 0d),
    };

    public static TransformGroup Transforms() => new() { Children = [new ScaleTransform(), new RotateTransform(), new TranslateTransform()] };

    private static Move Think(TimeSpan delay) =>
        Repeat(1.2, new SineEaseInOut(), delay, Frame(0, [Opacity(0.25), Y(0)]), Frame(0.5, [Opacity(1), Y(-1.5)]), Frame(1, [Opacity(0.25), Y(0)]));

    private static Move Enter(double seconds, Easing? easing, params KeyFrame[] frames) =>
        new(Animated(seconds, easing, TimeSpan.Zero, frames), false);

    private static Move Repeat(double seconds, Easing? easing, TimeSpan delay, params KeyFrame[] frames)
    {
        var animation = Animated(seconds, easing, delay, frames);
        animation.IterationCount = new IterationCount(Endless);
        animation.PlaybackBehavior = PlaybackBehavior.OnlyIfVisible;

        return new Move(animation, true);
    }

    private static Animation Animated(double seconds, Easing? easing, TimeSpan delay, params KeyFrame[] frames)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(seconds),
            Delay = delay,
            FillMode = FillMode.Backward,
            PlaybackBehavior = PlaybackBehavior.OnlyIfVisible,
        };

        if (easing is not null)
        {
            animation.Easing = easing;
        }

        animation.Children.AddRange(frames);

        return animation;
    }

    private static KeyFrame Frame(double cue, Setter[] setters)
    {
        var frame = new KeyFrame { Cue = new Cue(cue) };
        frame.Setters.AddRange(setters);

        return frame;
    }

    private static Setter Opacity(double value) => Set(Visual.OpacityProperty, value);

    private static Setter X(double value) => Set(TranslateTransform.XProperty, value);

    private static Setter Y(double value) => Set(TranslateTransform.YProperty, value);

    private static Setter Dash(double value) => Set(Avalonia.Controls.Shapes.Shape.StrokeDashOffsetProperty, value);

    private static Setter Angle(double value) => Set(RotateTransform.AngleProperty, value);

    private static Setter ScaleY(double value) => Set(ScaleTransform.ScaleYProperty, value);

    private static Setter[] Scale(double value) => [Set(ScaleTransform.ScaleXProperty, value), Set(ScaleTransform.ScaleYProperty, value)];

    private static Setter Set(AvaloniaProperty property, object value) => new(property, value);

    internal sealed record Move(Animation Animation, bool Repeats)
    {
        public RelativePoint Origin { get; init; } = RelativePoint.Center;

        public AvaloniaList<double> Dashes { get; init; } = [];
    }

    internal sealed record Pose(AvaloniaProperty Property, object Value);
}
