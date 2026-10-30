using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Components.UI.Markdown;

public sealed class CodeText : SelectableTextBlock
{
    public static readonly RoutedEvent<RoutedEventArgs> PaintedEvent =
        RoutedEvent.Register<CodeText, RoutedEventArgs>("Painted", RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> SelectedEvent =
        RoutedEvent.Register<CodeText, RoutedEventArgs>("Selected", RoutingStrategies.Bubble);

    public const int NumberedLines = 30;

    private const double GutterGap = 14;

    private readonly string code;
    private readonly string language;
    private readonly int[] starts;
    private List<(int Start, CodeInk Ink)> painted = [];
    private Action stop = () => { };
    private (TextLayout Code, TextLayout Numbers)? numbers;
    private bool dragging;

    public CodeText(string code, string language)
    {
        this.code = code;
        this.language = language;
        starts = LineStarts(code);
        Text = code;
        ActualThemeVariantChanged += (_, _) => InvalidateTextLayout();
        AddHandler(PointerMovedEvent, KeepDragging, RoutingStrategies.Tunnel, handledEventsToo: true);

        if (Highlights(language))
        {
            CodeColors.Cached(code, language).Match(
                lines =>
                {
                    Paint(lines);

                    return true;
                },
                () => false);
        }
    }

    public bool IsPainted => painted.Count > 0;

    public bool IsNumbered => starts.Length >= NumberedLines;

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public static bool Highlights(string language) => language.Trim().Length > 0;

    public CodeInk InkAt(int index)
    {
        var at = Segment(painted, index);

        return at < 0 ? CodeInk.Plain : painted[at].Ink;
    }

    public int StartOf(int line) => starts[Math.Clamp(line, 1, starts.Length) - 1];

    public IReadOnlyList<string> Numbers() => IsNumbered ? Gutter(TextLayout).Split('\n') : [];

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        base.RenderTextLayout(context, origin);

        if (!IsNumbered)
        {
            return;
        }

        var layout = TextLayout;

        if (numbers is not { } shown || !ReferenceEquals(shown.Code, layout))
        {
            shown = (layout, NumberLayout(layout));
            numbers = shown;
        }

        shown.Numbers.Draw(context, new Point(origin.X - Padding.Left, origin.Y));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Number();

        if (!IsPainted && Highlights(language))
        {
            _ = PaintAsync();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FontSizeProperty || change.Property == FontFamilyProperty)
        {
            Number();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        dragging = e.Pointer.Captured == this && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!dragging)
        {
            return;
        }

        dragging = false;

        if (SelectedText.Length > 0)
        {
            RaiseEvent(new RoutedEventArgs(SelectedEvent));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override TextLayout CreateTextLayout(string? text)
    {
        if (painted.Count == 0 || text != code)
        {
            return base.CreateTextLayout(text);
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var plain = new GenericTextRunProperties(typeface, FontSize, TextDecorations, Foreground, null, BaselineAlignment.Baseline, null, FontFeatures);
        var paragraph = new GenericTextParagraphProperties(FlowDirection, TextAlignment, true, false, plain, TextWrapping, LineHeight, 0, LetterSpacing);
        var dark = Themes.IsDark(ActualThemeVariant);
        var styles = new Dictionary<uint, GenericTextRunProperties>();
        var runs = painted.Select(segment => (segment.Start, Style(styles, typeface, dark ? segment.Ink.Dark : segment.Ink.Light, plain))).ToList();
        var size = GetMaxSizeFromConstraint();

        return new TextLayout(new PaintedSource(code, runs), paragraph, TextTrimming, size.Width, size.Height, MaxLines);
    }

    private static int Segment<T>(List<(int Start, T Value)> segments, int index)
    {
        var low = 0;
        var high = segments.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (segments[middle].Start <= index)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    private void KeepDragging(object? sender, PointerEventArgs e)
    {
        if (!dragging || !e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Pointer.Captured != this)
        {
            e.Pointer.Capture(this);
        }

        OnPointerMoved(e);
    }

    private async Task PaintAsync()
    {
        using var painting = new CancellationTokenSource();
        var token = painting.Token;
        stop = painting.Cancel;

        try
        {
            var lines = await CodeColors.LinesAsync(code, language, token);
            await Dispatcher.UIThread.InvokeAsync(() => Paint(lines), DispatcherPriority.Background, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            stop = () => { };
        }
        finally
        {
            stop = () => { };
        }
    }

    private static int[] LineStarts(string code)
    {
        var found = new List<int> { 0 };

        for (var index = 0; index < code.Length; index++)
        {
            if (code[index] == '\n')
            {
                found.Add(index + 1);
            }
        }

        return [.. found];
    }

    private void Number()
    {
        if (!IsNumbered)
        {
            return;
        }

        var digits = new TextLayout(new string('8', starts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length), new Typeface(FontFamily), FontSize, Foreground);
        Padding = new Thickness(digits.Width + GutterGap, Padding.Top, Padding.Right, Padding.Bottom);
        numbers = null;
    }

    private string Gutter(TextLayout layout) =>
        string.Join('\n', layout.TextLines.Select(line => Array.BinarySearch(starts, line.FirstTextSourceIndex) is var at and >= 0 ? (at + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty));

    private TextLayout NumberLayout(TextLayout layout)
    {
        var brush = this.TryFindResource("TextTertiaryBrush", ActualThemeVariant, out var found) && found is IBrush resolved ? resolved : Foreground;

        return new TextLayout(Gutter(layout), new Typeface(FontFamily), FontSize, brush, TextAlignment.Right, TextWrapping.NoWrap, maxWidth: Math.Max(0, Padding.Left - GutterGap), lineHeight: LineHeight);
    }

    private void Paint(IReadOnlyList<IReadOnlyList<CodeSpan>> lines)
    {
        var segments = new List<(int Start, CodeInk Ink)>();
        var at = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            at += index > 0 ? 1 : 0;

            foreach (var span in lines[index])
            {
                if (segments.Count == 0 || segments[^1].Ink != span.Ink)
                {
                    segments.Add((at, span.Ink));
                }

                at += span.Text.Length;
            }
        }

        painted = segments;
        InvalidateTextLayout();
        RaiseEvent(new RoutedEventArgs(PaintedEvent));
    }

    private GenericTextRunProperties Style(Dictionary<uint, GenericTextRunProperties> styles, Typeface typeface, uint color, GenericTextRunProperties plain)
    {
        if (color == 0)
        {
            return plain;
        }

        if (!styles.TryGetValue(color, out var style))
        {
            style = new GenericTextRunProperties(typeface, FontSize, TextDecorations, new ImmutableSolidColorBrush(Color.FromUInt32(color)), null, BaselineAlignment.Baseline, null, FontFeatures);
            styles[color] = style;
        }

        return style;
    }

    private readonly struct PaintedSource(string text, List<(int Start, GenericTextRunProperties Value)> runs) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= text.Length)
            {
                return new TextEndOfParagraph();
            }

            var at = Math.Max(0, Segment(runs, textSourceIndex));
            var end = at + 1 < runs.Count ? Math.Min(text.Length, runs[at + 1].Start) : text.Length;

            return new TextCharacters(text.AsMemory(textSourceIndex, Math.Max(1, end - textSourceIndex)), runs[at].Value);
        }
    }
}
