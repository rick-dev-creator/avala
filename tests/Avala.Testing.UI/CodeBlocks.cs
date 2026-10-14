using Avala.Components.UI.Markdown;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Avala.Testing.UI;

public static class CodeBlocks
{
    public static Task PaintedAsync(CodeText code)
    {
        if (code.IsPainted)
        {
            return Task.CompletedTask;
        }

        var painted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        code.AddHandler(CodeText.PaintedEvent, (_, _) => painted.TrySetResult());

        return painted.Task;
    }

    public static Task MarkedAsync(CodeFile file)
    {
        if (file.IsMarked)
        {
            return Task.CompletedTask;
        }

        var marked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        file.AddHandler(CodeFile.MarkedEvent, (_, _) => marked.TrySetResult());

        return marked.Task;
    }

    public static Color? ColorOf(CodeText code, string word, bool last = false)
    {
        var runs = code.TextLayout.TextLines
            .SelectMany(line => line.TextRuns)
            .OfType<ShapedTextRun>()
            .Where(run => run.Text.Span.Trim().SequenceEqual(word));
        var run = last ? runs.LastOrDefault() : runs.FirstOrDefault();

        return (run?.Properties.ForegroundBrush as ISolidColorBrush)?.Color;
    }
}
