using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace Avala.Components.UI.Markdown;

public sealed class CodeText : SelectableTextBlock
{
    public static readonly RoutedEvent<RoutedEventArgs> PaintedEvent =
        RoutedEvent.Register<CodeText, RoutedEventArgs>("Painted", RoutingStrategies.Bubble);

    private static readonly string[] Roles = ["keyword", "string", "number", "type", "function", "comment", "deleted"];

    private readonly string code;
    private readonly string language;
    private List<(int Start, string Role)> painted = [];
    private Action stop = () => { };

    public CodeText(string code, string language)
    {
        this.code = code;
        this.language = language;
        Text = code;
        ActualThemeVariantChanged += (_, _) => InvalidateTextLayout();

        if (Highlights(code, language))
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

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public static bool Highlights(string code, string language) =>
        language.Trim().Length > 0 && CodeColors.Count(code) <= CodeColors.HighlightedLines;

    public string RoleAt(int index)
    {
        var at = Segment(painted, index);

        return at < 0 ? string.Empty : painted[at].Role;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!IsPainted && Highlights(code, language))
        {
            _ = PaintAsync();
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
        var styles = Roles.ToDictionary(role => role, role => Style(typeface, role));
        var runs = painted.Select(segment => (segment.Start, styles.GetValueOrDefault(segment.Role, plain))).ToList();
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

    private void Paint(IReadOnlyList<IReadOnlyList<CodeSpan>> lines)
    {
        var segments = new List<(int Start, string Role)>();
        var at = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            at += index > 0 ? 1 : 0;

            foreach (var span in lines[index])
            {
                if (segments.Count == 0 || segments[^1].Role != span.Role)
                {
                    segments.Add((at, span.Role));
                }

                at += span.Text.Length;
            }
        }

        painted = segments;
        InvalidateTextLayout();
        RaiseEvent(new RoutedEventArgs(PaintedEvent));
    }

    private GenericTextRunProperties Style(Typeface typeface, string role)
    {
        var brush = this.TryFindResource($"Syntax{char.ToUpperInvariant(role[0])}{role[1..]}Brush", ActualThemeVariant, out var found) && found is IBrush resolved
            ? resolved
            : Foreground;
        var face = role == "comment" ? new Typeface(typeface.FontFamily, FontStyle.Italic, typeface.Weight, typeface.Stretch) : typeface;

        return new GenericTextRunProperties(face, FontSize, TextDecorations, brush, null, BaselineAlignment.Baseline, null, FontFeatures);
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
