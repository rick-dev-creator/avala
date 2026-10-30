using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Components.UI.Markdown;

public sealed class CodeDiff : StackPanel
{
    public static readonly RoutedEvent<RoutedEventArgs> PaintedEvent =
        RoutedEvent.Register<CodeDiff, RoutedEventArgs>("Painted", RoutingStrategies.Bubble);

    public static readonly StyledProperty<string> DiffProperty =
        AvaloniaProperty.Register<CodeDiff, string>(nameof(Diff), string.Empty);

    public static readonly StyledProperty<string> LanguageProperty =
        AvaloniaProperty.Register<CodeDiff, string>(nameof(Language), string.Empty);

    private List<Row> rows = [];
    private List<IReadOnlyList<CodeSpan>>? colors;
    private CancellationTokenSource? painting;

    public CodeDiff() => ActualThemeVariantChanged += (_, _) => Recolor();

    public string Diff
    {
        get => GetValue(DiffProperty);
        set => SetValue(DiffProperty, value);
    }

    public string Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    public bool IsPainted => colors is not null;

    public IReadOnlyList<TextBlock> Lines => [.. rows.Select(row => row.Block)];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DiffProperty || change.Property == LanguageProperty)
        {
            Show();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!IsPainted)
        {
            Paint();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private static Row Read(string line)
    {
        var text = line.TrimEnd('\r');
        var mark = text.Length > 0 && text[0] is '+' or '-' or ' ' ? text[0] : '\0';
        var block = new TextBlock { Text = text, Classes = { "line" } };

        if (mark == '+')
        {
            block.Classes.Add("added");
        }
        else if (mark == '-')
        {
            block.Classes.Add("removed");
        }

        return new Row(mark, mark == '\0' ? text : text[1..], block);
    }

    private static string Version(List<Row> rows, char changed) =>
        string.Join('\n', rows.Where(row => row.Mark == ' ' || row.Mark == changed).Select(row => row.Code));

    private void Show()
    {
        Stop();
        colors = null;
        rows = [.. Diff.Split('\n').Select(Read)];
        Children.Clear();
        Children.AddRange(rows.Select(row => row.Block));
        Paint();
    }

    private void Paint()
    {
        if (!CodeText.Highlights(Language) || rows.Count == 0 || VisualRoot is null)
        {
            return;
        }

        var older = CodeColors.Cached(Version(rows, '-'), Language);
        var newer = CodeColors.Cached(Version(rows, '+'), Language);

        if (older.IsSome && newer.IsSome)
        {
            Apply(older.Match(found => found, () => []), newer.Match(found => found, () => []));

            return;
        }

        _ = PaintAsync();
    }

    private async Task PaintAsync()
    {
        using var source = new CancellationTokenSource();
        painting = source;
        var shown = rows;

        try
        {
            var older = await CodeColors.LinesAsync(Version(shown, '-'), Language, source.Token);
            var newer = await CodeColors.LinesAsync(Version(shown, '+'), Language, source.Token);
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (ReferenceEquals(shown, rows))
                    {
                        Apply(older, newer);
                    }
                },
                DispatcherPriority.Background,
                source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            colors = null;
        }
        finally
        {
            if (ReferenceEquals(painting, source))
            {
                painting = null;
            }
        }
    }

    private void Stop()
    {
        painting?.Cancel();
        painting = null;
    }

    private void Apply(IReadOnlyList<IReadOnlyList<CodeSpan>> older, IReadOnlyList<IReadOnlyList<CodeSpan>> newer)
    {
        var (old, fresh) = (0, 0);
        var lines = new List<IReadOnlyList<CodeSpan>>(rows.Count);

        foreach (var mark in rows.Select(row => row.Mark))
        {
            lines.Add(mark switch
            {
                '-' => At(older, old),
                '\0' => [],
                _ => At(newer, fresh),
            });
            old += mark is '-' or ' ' ? 1 : 0;
            fresh += mark is '+' or ' ' ? 1 : 0;
        }

        colors = lines;
        Recolor();
        RaiseEvent(new RoutedEventArgs(PaintedEvent));
    }

    private static IReadOnlyList<CodeSpan> At(IReadOnlyList<IReadOnlyList<CodeSpan>> lines, int index) =>
        index < lines.Count ? lines[index] : [];

    private void Recolor()
    {
        if (colors is null)
        {
            return;
        }

        var dark = Themes.IsDark(ActualThemeVariant);

        foreach (var (row, spans) in rows.Zip(colors))
        {
            if (spans.Any(span => !span.Ink.IsPlain))
            {
                row.Block.Inlines = Inlines(row.Mark, spans, dark);
            }
        }
    }

    private static InlineCollection Inlines(char mark, IReadOnlyList<CodeSpan> spans, bool dark)
    {
        var inlines = new InlineCollection { new Run(mark.ToString()) };
        inlines.AddRange(spans.Select(span => Painted(span.Text, dark ? span.Ink.Dark : span.Ink.Light)));

        return inlines;
    }

    private static Run Painted(string text, uint color) =>
        color == 0 ? new Run(text) : new Run(text) { Foreground = new ImmutableSolidColorBrush(Color.FromUInt32(color)) };

    private sealed record Row(char Mark, string Code, TextBlock Block);
}
