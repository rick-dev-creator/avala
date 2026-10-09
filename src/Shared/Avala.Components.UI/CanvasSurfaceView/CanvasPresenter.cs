using System.Globalization;
using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Components.UI;

public sealed class CanvasPresenter : Panel
{
    public static readonly StyledProperty<CanvasRendering> RenderingProperty =
        AvaloniaProperty.Register<CanvasPresenter, CanvasRendering>(nameof(Rendering), CanvasRendering.Nothing);

    public static readonly AttachedProperty<TimeProvider> ClockProperty =
        AvaloniaProperty.RegisterAttached<CanvasPresenter, Control, TimeProvider>("Clock", TimeProvider.System, inherits: true);

    private CanvasRendering presented = CanvasRendering.Nothing;
    private int arrival;

    public CanvasRendering Rendering
    {
        get => GetValue(RenderingProperty);
        set => SetValue(RenderingProperty, value);
    }

    public bool IsArriving => Children.Count > 1;

    public static TimeProvider GetClock(Control control) => control.GetValue(ClockProperty);

    public static void SetClock(Control control, TimeProvider clock) => control.SetValue(ClockProperty, clock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RenderingProperty && this.IsAttachedToVisualTree())
        {
            Present(change.GetNewValue<CanvasRendering>());
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!ReferenceEquals(Rendering, presented))
        {
            Present(Rendering);
        }
    }

    private void Present(CanvasRendering rendering)
    {
        if (rendering is not { IsNothing: false })
        {
            Settle();
            Children.Clear();
            presented = CanvasRendering.Nothing;
        }
        else if (!rendering.IsOffered)
        {
            Arrive(rendering, this.FindDataTemplate(rendering)?.Build(rendering) ?? CanvasSource.Show(CanvasSource.NotOffered(rendering), rendering.Content));
        }
        else if (!rendering.IsRenderable)
        {
            Arrive(rendering, CanvasSource.Show(TooLarge(rendering), rendering.Content));
        }
        else if (this.FindDataTemplate(rendering) is not { } renderer)
        {
            Arrive(rendering, CanvasSource.Show($"Avala has no renderer for {rendering.Essence}. Showing its source.", rendering.Content));
        }
        else if (renderer.Build(rendering) is { } drawn)
        {
            Arrive(rendering, drawn);
        }
        else if (rendering.IsFinal || !rendering.Follows(presented))
        {
            Arrive(rendering, CanvasSource.Show(Unrenderable(rendering), rendering.Content));
        }
    }

    private void Arrive(CanvasRendering rendering, Control layer)
    {
        Settle();
        presented = rendering;
        Children.Add(layer);

        if (Children.Count > 1 && Motion.GetIsReduced(this))
        {
            Settle();
        }
        else if (Children.Count > 1)
        {
            FadeIn(layer, arrival);
        }
    }

    private void Settle()
    {
        arrival++;

        while (Children.Count > 1)
        {
            Children.RemoveAt(0);
        }

        if (Children.Count > 0)
        {
            Children[0].Transitions = null;
            Children[0].Opacity = 1;
        }
    }

    private void FadeIn(Control layer, int fading)
    {
        var duration = this.TryFindResource("StreamDuration", out var value) && value is TimeSpan span ? span : TimeSpan.Zero;
        layer.Opacity = 0;
        layer.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = new CubicEaseOut() }];
        layer.Opacity = 1;
        ITimer? arrived = null;
        arrived = GetClock(this).CreateTimer(
            _ => Dispatcher.UIThread.Post(() =>
            {
                arrived?.Dispose();

                if (fading == arrival)
                {
                    Settle();
                }
            }),
            null,
            duration,
            Timeout.InfiniteTimeSpan);
    }

    private static string TooLarge(CanvasRendering rendering) =>
        string.Create(CultureInfo.InvariantCulture, $"This canvas is too large to render ({rendering.Content.Length / 1024:N0} KB). Showing the start of its source.");

    private static string Unrenderable(CanvasRendering rendering) =>
        rendering.IsFinal
            ? $"This {CanvasMediaTypes.Label(rendering.MediaType)} could not be rendered. Showing its source."
            : "This version is still incomplete. Showing its source.";
}
