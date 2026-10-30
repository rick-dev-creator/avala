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
    Ping,
    Boot,
    Glitch,
    Close,
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

    public static readonly AttachedProperty<object> SwapProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, object>("Swap");

    private static readonly AttachedProperty<Playback?> SwappingProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, Playback?>("Swapping");

    private static readonly AttachedProperty<Playback?> PlaybackProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, Playback?>("Playback");

    private static readonly AttachedProperty<Playback?> LoopingProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, Playback?>("Looping");

    private static readonly AttachedProperty<Sightline?> SightlineProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, Sightline?>("Sightline");

    private static readonly AttachedProperty<bool> SettlesProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("Settles");

    private static readonly AttachedProperty<bool> IsWatchedProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("IsWatched");

    static Motion()
    {
        EnterProperty.Changed.AddClassHandler<Visual>((visual, _) => Watch(visual));
        LoopProperty.Changed.AddClassHandler<Visual>((visual, _) => Watch(visual));
        WhenProperty.Changed.AddClassHandler<Visual>((visual, _) => Refresh(visual));
        SwapProperty.Changed.AddClassHandler<Visual>((visual, change) => Swap(visual, change.OldValue, change.NewValue));
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

    public static object GetSwap(Visual visual) => visual.GetValue(SwapProperty);

    public static void SetSwap(Visual visual, object value) => visual.SetValue(SwapProperty, value);

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

    internal static void Withhold(Visual container) => container.SetValue(SettlesProperty, true);

    internal static void Release(Visual container)
    {
        container.ClearValue(SettlesProperty);

        foreach (var watched in container.GetSelfAndVisualDescendants().Where(visual => visual.GetValue(IsWatchedProperty)))
        {
            Refresh(watched);
        }
    }

    internal static bool IsSettling(Visual visual)
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
            visual.DetachedFromVisualTree += (_, _) =>
            {
                Stop(visual);
                Unsight(visual);
            };
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

            if (GetLoop(visual) != MotionLoop.None)
            {
                Loop(visual);
            }
        }
    }

    private static void Swap(Visual visual, object? before, object? after)
    {
        Release(visual, SwappingProperty);

        if (before is null || after is null || GetIsReduced(visual) || !visual.IsAttachedToVisualTree())
        {
            return;
        }

        var playback = new Playback();
        visual.SetValue(SwappingProperty, playback);
        playback.Play(visual, MotionCatalog.SwapIn);
    }

    private static void Loop(Visual visual)
    {
        Release(visual, LoopingProperty);

        if (visual.GetValue(SightlineProperty) is not { } sightline)
        {
            sightline = new Sightline(visual, () => Loop(visual));
            visual.SetValue(SightlineProperty, sightline);
        }

        if (sightline.IsClear && visual.GetValue(PlaybackProperty) is not null && !GetIsReduced(visual) && GetWhen(visual) && MotionCatalog.Loops.TryGetValue(GetLoop(visual), out var loop))
        {
            var looping = new Playback();
            visual.SetValue(LoopingProperty, looping);
            looping.Play(visual, loop);
        }
    }

    private static void Stop(Visual visual)
    {
        Release(visual, PlaybackProperty);
        Release(visual, LoopingProperty);
    }

    private static void Unsight(Visual visual)
    {
        if (visual.GetValue(SightlineProperty) is { } sightline)
        {
            visual.ClearValue(SightlineProperty);
            sightline.Close();
        }
    }

    private static void Release(Visual visual, AttachedProperty<Playback?> property)
    {
        if (visual.GetValue(property) is { } playback)
        {
            visual.ClearValue(property);
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
