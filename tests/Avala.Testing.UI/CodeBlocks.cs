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

    public static IBrush? ForegroundOf(CodeText code, string word) =>
        code.TextLayout.TextLines
            .SelectMany(line => line.TextRuns)
            .OfType<ShapedTextRun>()
            .FirstOrDefault(run => run.Text.Span.Trim().SequenceEqual(word))
            ?.Properties.ForegroundBrush;
}
