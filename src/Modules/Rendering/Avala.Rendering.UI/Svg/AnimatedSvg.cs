using Avala.Components.UI;
using Avala.Components.UI.Theme;
using Avala.Rendering.Animation;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Svg;
using Svg.Skia;
using Path = Avalonia.Controls.Shapes.Path;
using SvgDrawing = Avalonia.Svg.Skia.Svg;

namespace Avala.Rendering.UI.Svg;

internal sealed class AnimatedSvg : Panel
{
    private static readonly Uri Assets = new($"avares://{typeof(AnimatedSvg).Assembly.GetName().Name}/");

    private readonly SvgTimeline timeline;
    private readonly SvgDrawing drawing;
    private readonly Button play;
    private TopLevel? host;
    private ITimer? ending;
    private bool inView = true;
    private bool playingOnce;
    private bool ended;

    public AnimatedSvg(SvgDocument document, SvgTimeline timeline)
    {
        this.timeline = timeline;
        Name = "Svg";
        Background = Brushes.Transparent;
        drawing = new SvgDrawing(Assets)
        {
            Name = "SvgDrawing",
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            AnimationBackend = SvgAnimationHostBackend.Manual,
        };
        drawing.LoadFromSvgDocument(document);
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

    public bool Moves => drawing.SkSvg is { HasAnimations: true } && drawing.Picture is not null;

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
        drawing.SkSvg?.SetAnimationTime(moment);
        drawing.InvalidateVisual();
    }

    private void PlayOnce()
    {
        playingOnce = true;
        Seek(TimeSpan.Zero);
        Refresh();
    }

    private void End()
    {
        ended = !timeline.Loops;
        playingOnce = false;
        Seek(timeline.End);
        Refresh();
    }

    private void Refresh()
    {
        var reduced = Motion.GetIsReduced(this);
        var shown = host is not null && inView && IsEffectivelyVisible && !Minimized(host);
        var playing = shown && (reduced ? playingOnce : !ended);
        play.IsVisible = reduced && !playingOnce && IsPointerOver;
        ending?.Dispose();
        ending = null;
        LayoutUpdated -= OnLayoutUpdated;

        if (playing != IsPlaying)
        {
            IsPlaying = playing;
            drawing.AnimationBackend = playing ? SvgAnimationHostBackend.Default : SvgAnimationHostBackend.Manual;
        }

        if (playing && (playingOnce || !timeline.Loops))
        {
            var elapsed = drawing.SkSvg?.AnimationTime ?? TimeSpan.Zero;
            var remaining = timeline.End > elapsed ? timeline.End - elapsed : TimeSpan.Zero;
            ending = CanvasPresenter.GetClock(this).CreateTimer(_ => Dispatcher.UIThread.Post(End), null, remaining, Timeout.InfiniteTimeSpan);
        }
        else if (!playing && host is not null && !IsEffectivelyVisible)
        {
            LayoutUpdated += OnLayoutUpdated;
        }
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
        var icon = new Path { Width = 12, Height = 12, Margin = new Thickness(2, 0, 0, 0), Stretch = Stretch.Uniform };
        icon.Bind(Path.DataProperty, icon.GetResourceObservable("IconPlay"));
        icon.Bind(Shape.FillProperty, icon.GetResourceObservable("TextPrimaryBrush"));
        var button = new Button
        {
            Name = "PlayAnimation",
            Content = icon,
            Width = 36,
            Height = 36,
            Padding = default,
            Focusable = false,
            IsVisible = false,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, "Play the animation");
        button.Bind(BackgroundProperty, button.GetResourceObservable("SurfacePopoverBrush"));
        button.Bind(Button.BorderBrushProperty, button.GetResourceObservable("EdgeFloatBrush"));
        button.Bind(Button.CornerRadiusProperty, button.GetResourceObservable("RadiusPill"));

        return button;
    }
}
