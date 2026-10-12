using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Elapsed;

public sealed class ElapsedText : TextBlock
{
    public static readonly StyledProperty<DateTimeOffset> SinceProperty =
        AvaloniaProperty.Register<ElapsedText, DateTimeOffset>(nameof(Since));

    public static readonly AttachedProperty<TimeProvider> ClockProperty =
        AvaloniaProperty.RegisterAttached<ElapsedText, Visual, TimeProvider>("Clock", TimeProvider.System, inherits: true);

    private readonly List<Visual> ancestors = [];
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

        if (change.Property == SinceProperty || change.Property == ClockProperty || change.Property == IsVisibleProperty)
        {
            Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ancestors.AddRange(this.GetVisualAncestors());

        foreach (var ancestor in ancestors)
        {
            ancestor.PropertyChanged += OnAncestorChanged;
        }

        Tick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        foreach (var ancestor in ancestors)
        {
            ancestor.PropertyChanged -= OnAncestorChanged;
        }

        ancestors.Clear();
        Stop();
    }

    private void OnAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty)
        {
            Tick();
        }
    }

    private void Tick()
    {
        Stop();

        if (!IsVisible || ancestors.Count == 0 || ancestors.Exists(ancestor => !ancestor.IsVisible))
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
