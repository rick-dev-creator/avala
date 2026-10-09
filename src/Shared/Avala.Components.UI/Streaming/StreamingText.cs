using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Avala.Components.UI.Streaming;

public sealed class StreamingText : SelectableTextBlock
{
    public static readonly StyledProperty<string> StreamProperty =
        AvaloniaProperty.Register<StreamingText, string>(nameof(Stream), string.Empty);

    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<StreamingText, bool>(nameof(IsLive));

    private const int FreshRuns = 8;
    private static readonly TimeSpan Arrival = TimeSpan.FromMilliseconds(260);

    private readonly Run settled = new();
    private readonly List<Run> fresh = [];
    private readonly InlineUIContainer caret;
    private string shown = string.Empty;

    public StreamingText()
    {
        var bar = new Border { Width = 2, Height = 16, Margin = new Thickness(2, 0, 0, -3) };
        bar.Classes.Add("caret");
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("AccentBrush"));
        caret = new InlineUIContainer(bar) { BaselineAlignment = BaselineAlignment.TextBottom };
        Inlines = [settled];
    }

    public string Stream
    {
        get => GetValue(StreamProperty);
        set => SetValue(StreamProperty, value);
    }

    public bool IsLive
    {
        get => GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    public int Arriving => fresh.Count;

    public bool ShowsCaret => Inlines?.Contains(caret) == true;

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StreamProperty)
        {
            Flow(Stream);
        }
        else if (change.Property == IsLiveProperty)
        {
            Settle();
        }
    }

    private void Flow(string text)
    {
        if (IsLive && text.Length > shown.Length && text.StartsWith(shown, StringComparison.Ordinal))
        {
            Arrive(text[shown.Length..]);
        }
        else if (text != shown)
        {
            settled.Text = text;
            fresh.Clear();
            Rebuild();
        }

        shown = text;
    }

    private void Arrive(string chunk)
    {
        var color = Foreground is ISolidColorBrush solid ? solid.Color : Colors.White;
        var brush = new SolidColorBrush(color, 0)
        {
            Transitions = [new DoubleTransition { Property = Brush.OpacityProperty, Duration = Arrival, Easing = new CubicEaseOut() }],
        };
        var run = new Run(chunk) { Foreground = brush };
        fresh.Add(run);
        Inlines!.Insert(fresh.Count, run);
        brush.Opacity = 1;

        if (fresh.Count > FreshRuns)
        {
            settled.Text += fresh[0].Text;
            Inlines.Remove(fresh[0]);
            fresh.RemoveAt(0);
        }
    }

    private void Settle()
    {
        settled.Text = shown;
        fresh.Clear();
        Rebuild();
    }

    private void Rebuild()
    {
        var inlines = Inlines!;
        inlines.Clear();
        inlines.Add(settled);
        inlines.AddRange(fresh);

        if (IsLive)
        {
            inlines.Add(caret);
        }
    }
}
