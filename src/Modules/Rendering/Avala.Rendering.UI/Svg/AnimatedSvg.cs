using Avala.Components.UI;
using Avala.Components.UI.Theme;
using Avala.Rendering.Animation;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Path = Avalonia.Controls.Shapes.Path;

namespace Avala.Rendering.UI.Svg;

internal sealed class AnimatedSvg : Panel
{
    private readonly SvgSource source;
    private readonly SvgTimeline timeline;
    private readonly Image drawing;
    private readonly Button play;
    private TopLevel? host;
    private long? lastFrame;
    private bool frameRequested;
    private bool inView = true;
    private bool playingOnce;
    private TimeSpan time;

    public AnimatedSvg(SvgSource source, SvgTimeline timeline)
    {
        this.source = source;
        this.timeline = timeline;
        Name = "Svg";
        Background = Brushes.Transparent;
        drawing = new Image
        {
            Name = "SvgDrawing",
            Source = new SvgImage { Source = source },
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
        play = Play();
        play.Click += (_, _) => PlayOnce();
        Children.Add(drawing);
        Children.Add(play);
        EffectiveViewportChanged += (_, change) =>
        {
            inView = change.EffectiveViewport.Intersects(new Rect(Bounds.Size));
            Refresh();
        };
    }

    private bool IsPlaying
    {
        get => PseudoClasses.Contains(":playing");
        set => PseudoClasses.Set(":playing", value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        host = TopLevel.GetTopLevel(this);
        host?.PropertyChanged += OnHostChanged;
        Hold();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        host?.PropertyChanged -= OnHostChanged;
        host = null;
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == Motion.IsReducedProperty)
        {
            playingOnce = false;
            Hold();
            Refresh();
        }
        else if (change.Property == IsPointerOverProperty || change.Property == IsVisibleProperty)
        {
            Refresh();
        }
    }

    private void Hold()
    {
        if (Motion.GetIsReduced(this) && !playingOnce)
        {
            Seek(timeline.End);
        }
    }

    private void Seek(TimeSpan moment)
    {
        time = !timeline.Loops && moment > timeline.End ? timeline.End : moment;
        source.Svg?.SetAnimationTime(time);
        drawing.InvalidateVisual();
    }

    private void PlayOnce()
    {
        playingOnce = true;
        lastFrame = null;
        Seek(TimeSpan.Zero);
        Refresh();
    }

    private void Refresh()
    {
        var reduced = Motion.GetIsReduced(this);
        var shown = host is not null && inView && IsEffectivelyVisible && !Minimized(host);
        IsPlaying = shown && (reduced ? playingOnce : timeline.Loops || time < timeline.End);
        play.IsVisible = reduced && !playingOnce && IsPointerOver;
        LayoutUpdated -= OnLayoutUpdated;

        if (!IsPlaying)
        {
            lastFrame = null;

            if (host is not null && !IsEffectivelyVisible)
            {
                LayoutUpdated += OnLayoutUpdated;
            }
        }
        else if (!frameRequested)
        {
            frameRequested = true;
            host!.RequestAnimationFrame(OnFrame);
        }
    }

    private void OnFrame(TimeSpan _)
    {
        frameRequested = false;

        if (!IsPlaying)
        {
            return;
        }

        var clock = CanvasPresenter.GetClock(this);
        var now = clock.GetTimestamp();
        Seek(time + (lastFrame is { } previous ? clock.GetElapsedTime(previous, now) : TimeSpan.Zero));
        lastFrame = now;

        if (playingOnce && time >= timeline.End)
        {
            playingOnce = false;
            Seek(timeline.End);
        }

        Refresh();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (IsEffectivelyVisible)
        {
            Refresh();
        }
    }

    private void OnHostChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Window.WindowStateProperty || change.Property == IsVisibleProperty)
        {
            Refresh();
        }
    }

    private static bool Minimized(TopLevel top) =>
        !top.IsVisible || top is Window { WindowState: WindowState.Minimized };

    private static Button Play()
    {
        var icon = new Path { Classes = { "icon" }, Stretch = Stretch.Uniform };
        icon.Bind(Path.DataProperty, icon.GetResourceObservable("IconPlay"));
        icon.Bind(Shape.FillProperty, icon.GetResourceObservable("TextPrimaryBrush"));
        var button = new Button
        {
            Name = "PlayAnimation",
            Classes = { "mini" },
            Content = icon,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, "Play the animation");
        button.Bind(BackgroundProperty, button.GetResourceObservable("SurfacePopoverBrush"));
        button.Bind(Button.CornerRadiusProperty, button.GetResourceObservable("RadiusPill"));

        return button;
    }
}
