using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Avala.Components.UI.Elapsed;

public sealed class ElapsedText : TextBlock
{
    public static readonly StyledProperty<DateTimeOffset> SinceProperty =
        AvaloniaProperty.Register<ElapsedText, DateTimeOffset>(nameof(Since));

    public static readonly AttachedProperty<TimeProvider> ClockProperty =
        AvaloniaProperty.RegisterAttached<ElapsedText, Visual, TimeProvider>("Clock", TimeProvider.System, inherits: true);

    private DispatcherTimer? ticking;

    public DateTimeOffset Since
    {
        get => GetValue(SinceProperty);
        set => SetValue(SinceProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public static TimeProvider GetClock(Visual visual) => visual.GetValue(ClockProperty);

    public static void SetClock(Visual visual, TimeProvider clock) => visual.SetValue(ClockProperty, clock);

    public static string Phrase(TimeSpan elapsed)
    {
        var whole = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

        return whole.TotalSeconds < 60 ? string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalSeconds}s")
            : whole.TotalMinutes < 60 ? string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalMinutes}m {whole.Seconds}s")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)whole.TotalHours}h {whole.Minutes}m");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SinceProperty || change.Property == ClockProperty || change.Property == IsVisibleProperty)
        {
            Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick();
        ticking = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        ticking.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ticking?.Stop();
        ticking = null;
    }

    private void Tick()
    {
        if (IsVisible)
        {
            Text = Phrase(GetClock(this).GetUtcNow() - Since);
        }
    }
}
