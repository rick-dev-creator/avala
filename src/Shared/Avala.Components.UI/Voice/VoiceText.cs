using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace Avala.Components.UI.Voice;

public sealed class VoiceText : TextBlock
{
    public static readonly StyledProperty<double> RevealedProperty =
        AvaloniaProperty.Register<VoiceText, double>(nameof(Revealed), 1);

    public static readonly TimeSpan PerWord = TimeSpan.FromMilliseconds(45);

    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(2.5);

    private static readonly IImmutableBrush Hidden = new ImmutableSolidColorBrush(Colors.Transparent);

    static VoiceText() => AffectsRender<VoiceText>(RevealedProperty);

    public double Revealed
    {
        get => GetValue(RevealedProperty);
        set => SetValue(RevealedProperty, value);
    }

    public static int Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    public static int Cut(string text, double revealed)
    {
        var words = Words(text);
        var shown = (int)Math.Ceiling(Math.Clamp(revealed, 0, 1) * words);

        if (shown >= words)
        {
            return text.Length;
        }

        var seen = 0;
        var inWord = false;

        for (var index = 0; index < text.Length; index++)
        {
            var space = text[index] == ' ';

            if (!space && !inWord && seen++ == shown)
            {
                return index;
            }

            inWord = !space;
        }

        return text.Length;
    }

    public static TimeSpan Duration(string text) => TimeSpan.FromTicks(Math.Min(Longest.Ticks, PerWord.Ticks * Words(text)));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && IsAttachedToVisualTree())
        {
            Speak();
        }
        else if (change.Property == RevealedProperty)
        {
            InvalidateTextLayout();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Speak();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Transitions = null;
        Revealed = 1;
    }

    protected override TextLayout CreateTextLayout(string? text)
    {
        if (text is not { Length: > 0 } || Cut(text, Revealed) is var cut && cut >= text.Length)
        {
            return base.CreateTextLayout(text);
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var shown = new GenericTextRunProperties(typeface, FontSize, TextDecorations, Foreground, null, BaselineAlignment.Baseline, null, FontFeatures);
        var hidden = new GenericTextRunProperties(typeface, FontSize, TextDecorations, Hidden, null, BaselineAlignment.Baseline, null, FontFeatures);
        var paragraph = new GenericTextParagraphProperties(FlowDirection, TextAlignment, true, false, shown, TextWrapping, LineHeight, 0, LetterSpacing);
        var size = GetMaxSizeFromConstraint();

        return new TextLayout(new Spoken(text, cut, shown, hidden), paragraph, TextTrimming, size.Width, size.Height, MaxLines);
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;

    private void Speak()
    {
        Transitions = null;

        if (Motion.GetIsReduced(this) || Text is not { Length: > 0 } text)
        {
            Revealed = 1;
            return;
        }

        Revealed = 0;
        Transitions = [new DoubleTransition { Property = RevealedProperty, Duration = Duration(text), Easing = new LinearEasing() }];
        Revealed = 1;
    }

    private readonly struct Spoken(string text, int cut, GenericTextRunProperties shown, GenericTextRunProperties hidden) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex) =>
            textSourceIndex >= text.Length
                ? new TextEndOfParagraph()
                : textSourceIndex < cut
                    ? new TextCharacters(text.AsMemory(textSourceIndex, cut - textSourceIndex), shown)
                    : new TextCharacters(text.AsMemory(textSourceIndex), hidden);
    }
}
