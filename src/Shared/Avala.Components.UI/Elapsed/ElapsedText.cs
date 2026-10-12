using System.Globalization;
using Avala.Components.UI.Theme;
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

    private Sightline? sightline;
    private ITimer? ticking;

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

    public static TimeSpan UntilNextChange(TimeSpan elapsed)
    {
        var whole = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        var step = whole.TotalHours < 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);

        return step - TimeSpan.FromTicks(whole.Ticks % step.Ticks);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SinceProperty || change.Property == ClockProperty)
        {
            Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        sightline = new Sightline(this, Tick);
        Tick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        sightline?.Close();
        sightline = null;
        Stop();
    }

    private void Tick()
    {
        Stop();

        if (sightline is not { IsClear: true })
        {
            return;
        }

        var clock = GetClock(this);
        var elapsed = clock.GetUtcNow() - Since;
        Text = Phrase(elapsed);
        ticking = clock.CreateTimer(_ => Dispatcher.UIThread.Post(Tick), null, UntilNextChange(elapsed), Timeout.InfiniteTimeSpan);
    }

    private void Stop()
    {
        ticking?.Dispose();
        ticking = null;
    }
}
