using Avala.Components.UI;
using Avala.Components.UI.Theme;
using Avala.Rendering.Animation;
using Avala.Sdk;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Svg.Skia;
using Svg.Skia;
using Path = Avalonia.Controls.Shapes.Path;
using SvgDrawing = Avalonia.Svg.Skia.Svg;

namespace Avala.Rendering.UI.Svg;

internal sealed class AnimatedSvg : Panel
{
    private static readonly Uri Assets = new($"avares://{typeof(AnimatedSvg).Assembly.GetName().Name}/");

    private readonly SvgModel model;
    private readonly SvgTimeline timeline;
    private readonly SvgDrawing drawing;
    private readonly Button play;
    private TopLevel? host;
    private ITimer? ending;
    private bool inView = true;
    private bool playingOnce;
    private bool ended;

    public AnimatedSvg(SvgModel model, SvgTimeline timeline)
    {
        this.model = model;
        this.timeline = timeline;
        Name = "Svg";
        Background = Brushes.Transparent;
        drawing = new SvgDrawing(Assets)
        {
            Name = "SvgDrawing",
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            AnimationBackend = SvgAnimationHostBackend.Manual,
        };
        _ = model.Sanitized.Area.Map(area => Sized(drawing, area));
        CanvasPresenter.SetIsDrawing(this, true);
        drawing.PropertyChanged += OnDrawingChanged;
        play = Play();
        play.Click += (_, _) => PlayOnce();
        Children.Add(SvgModel.Fit(drawing));
        Children.Add(play);
        EffectiveViewportChanged += (_, change) =>
        {
            var seen = change.EffectiveViewport.Intersects(new Rect(Bounds.Size));

            if (seen != inView)
            {
                inView = seen;
                Refresh();
            }
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
        _ = LoadAsync(model.ClaimAsync());
        Hold();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        host?.PropertyChanged -= OnHostChanged;
        host = null;
        Refresh();
        model.Release();
    }

    private async Task LoadAsync(Task<Option<SvgSource>> loading)
    {
        var loaded = await loading;

        if (drawing.SvgSource is null && host is not null)
        {
            _ = loaded.Map(source => drawing.SvgSource = source);
        }

        if (loaded.IsNone && CanvasPresenter.GetIsDrawing(this))
        {
            CanvasPresenter.Drawn(this);
        }
    }

    private void OnDrawingChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == BoundsProperty && drawing.Bounds.Width > 0 && CanvasPresenter.GetIsDrawing(this))
        {
            CanvasPresenter.Drawn(this);
            Hold();
            Refresh();
        }
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
        var playing = IsShown() && (reduced ? playingOnce : !ended);
        play.IsVisible = reduced && !playingOnce && IsPointerOver;
        StopWaiting();
        Switch(playing);

        if (playing && (playingOnce || !timeline.Loops))
        {
            EndWithTheTimeline();
        }
        else if (!playing && host is not null && !IsEffectivelyVisible)
        {
            LayoutUpdated += OnLayoutUpdated;
        }
    }

    private bool IsShown() => host is { } top && inView && IsEffectivelyVisible && !Minimized(top);

    private void StopWaiting()
    {
        ending?.Dispose();
        ending = null;
        LayoutUpdated -= OnLayoutUpdated;
    }

    private void Switch(bool playing)
    {
        if (playing != IsPlaying)
        {
            IsPlaying = playing;
            drawing.AnimationBackend = playing ? SvgAnimationHostBackend.Default : SvgAnimationHostBackend.Manual;
        }
    }

    private void EndWithTheTimeline()
    {
        var elapsed = drawing.SkSvg?.AnimationTime ?? TimeSpan.Zero;
        var remaining = timeline.End > elapsed ? timeline.End - elapsed : TimeSpan.Zero;
        ending = CanvasPresenter.GetClock(this).CreateTimer(_ => Dispatcher.UIThread.Post(End), null, remaining, Timeout.InfiniteTimeSpan);
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

    private static SvgDrawing Sized(SvgDrawing drawing, Sizing.SvgArea area)
    {
        drawing.Width = area.Width;
        drawing.Height = area.Height;

        return drawing;
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
