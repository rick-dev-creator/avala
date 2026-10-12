using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Controls;

namespace Avala.Components.UI.Theme;

public enum MotionEntrance
{
    None,
    Fade,
    Arrive,
    Gentle,
    Sheet,
    Pop,
    Fold,
    Rise,
    Stamp,
    Scale,
    Draw,
    Linger,
}

public enum MotionLoop
{
    None,
    Pulse,
    Spin,
    Think,
    ThinkSecond,
    ThinkThird,
    Shimmer,
    ShimmerText,
    Caret,
    Drift,
}

public sealed class Motion
{
    public static readonly AttachedProperty<bool> IsReducedProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("IsReduced", inherits: true);

    public static readonly AttachedProperty<MotionEntrance> EnterProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, MotionEntrance>("Enter");

    public static readonly AttachedProperty<MotionLoop> LoopProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, MotionLoop>("Loop");

    public static readonly AttachedProperty<bool> WhenProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("When", defaultValue: true);

    private static readonly AttachedProperty<Playback?> PlaybackProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, Playback?>("Playback");

    private static readonly AttachedProperty<bool> SettlesProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("Settles");

    private static readonly AttachedProperty<bool> IsWatchedProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("IsWatched");

    static Motion()
    {
        EnterProperty.Changed.AddClassHandler<Visual>((visual, _) => Watch(visual));
        LoopProperty.Changed.AddClassHandler<Visual>((visual, _) => Watch(visual));
        WhenProperty.Changed.AddClassHandler<Visual>((visual, _) => Refresh(visual));
    }

    private Motion()
    {
    }

    public static bool GetIsReduced(Visual visual) => visual.GetValue(IsReducedProperty);

    public static void SetIsReduced(Visual visual, bool value) => visual.SetValue(IsReducedProperty, value);

    public static MotionEntrance GetEnter(Visual visual) => visual.GetValue(EnterProperty);

    public static void SetEnter(Visual visual, MotionEntrance value) => visual.SetValue(EnterProperty, value);

    public static MotionLoop GetLoop(Visual visual) => visual.GetValue(LoopProperty);

    public static void SetLoop(Visual visual, MotionLoop value) => visual.SetValue(LoopProperty, value);

    public static bool GetWhen(Visual visual) => visual.GetValue(WhenProperty);

    public static void SetWhen(Visual visual, bool value) => visual.SetValue(WhenProperty, value);

    public static bool IsPlaying(Visual visual) => visual.GetValue(PlaybackProperty) is { IsMoving: true };

    public static void Settle(Layoutable content)
    {
        content.SetValue(SettlesProperty, true);
        content.LayoutUpdated += Settled;

        void Settled(object? sender, EventArgs e)
        {
            content.LayoutUpdated -= Settled;
            content.ClearValue(SettlesProperty);
        }
    }

    private static bool IsSettling(Visual visual)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current.GetValue(SettlesProperty))
            {
                return true;
            }
        }

        return false;
    }

    private static void Watch(Visual visual)
    {
        if (!visual.GetValue(IsWatchedProperty))
        {
            visual.SetValue(IsWatchedProperty, true);
            visual.AttachedToVisualTree += (_, _) => Refresh(visual);
            visual.DetachedFromVisualTree += (_, _) => Stop(visual);
            visual.PropertyChanged += (_, change) =>
            {
                if (change.Property == IsReducedProperty)
                {
                    Refresh(visual);
                }
            };
        }

        Refresh(visual);
    }

    private static void Refresh(Visual visual)
    {
        if (!visual.GetValue(IsWatchedProperty))
        {
            return;
        }

        Stop(visual);

        if (TopLevel.GetTopLevel(visual) is null)
        {
            return;
        }

        var playback = new Playback();
        visual.SetValue(PlaybackProperty, playback);

        if (GetIsReduced(visual))
        {
            if (MotionCatalog.Stills.TryGetValue(GetLoop(visual), out var still))
            {
                playback.Hold(visual, still);
            }

            return;
        }

        if (GetWhen(visual))
        {
            if (!IsSettling(visual) && MotionCatalog.Entrances.TryGetValue(GetEnter(visual), out var entrance))
            {
                playback.Play(visual, entrance);
            }

            if (MotionCatalog.Loops.TryGetValue(GetLoop(visual), out var loop))
            {
                playback.Play(visual, loop);
            }
        }
    }

    private static void Stop(Visual visual)
    {
        if (visual.GetValue(PlaybackProperty) is { } playback)
        {
            visual.ClearValue(PlaybackProperty);
            playback.Dispose();
        }
    }

    private sealed class Playback : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly List<IDisposable> held = [];

        public bool IsMoving { get; private set; }

        public void Hold(Visual visual, MotionCatalog.Pose pose) =>
            Keep(visual.SetValue(pose.Property, pose.Value, BindingPriority.Style));

        public void Play(Visual visual, MotionCatalog.Move move)
        {
            IsMoving = true;

            if (visual.RenderTransform is null)
            {
                Keep(visual.SetValue(Visual.RenderTransformProperty, MotionCatalog.Transforms(), BindingPriority.Style));
            }

            if (move.Origin != RelativePoint.Center)
            {
                Keep(visual.SetValue(Visual.RenderTransformOriginProperty, move.Origin, BindingPriority.Style));
            }

            if (move.Dashes.Count > 0 && visual is Shape shape)
            {
                Keep(shape.SetValue(Shape.StrokeDashArrayProperty, move.Dashes, BindingPriority.Style));
            }

            _ = RunAsync(visual, move, cancellation.Token);
        }

        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();

            foreach (var value in held)
            {
                value.Dispose();
            }

            held.Clear();
        }

        private void Keep(IDisposable? value)
        {
            if (value is not null)
            {
                held.Add(value);
            }
        }

        private static async Task RunAsync(Visual visual, MotionCatalog.Move move, CancellationToken token)
        {
            do
            {
                await move.Animation.RunAsync(visual, token);
            }
            while (move.Repeats && !token.IsCancellationRequested);
        }
    }
}
