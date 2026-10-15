using System.Text;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;

namespace Avala.Workbench.Timeline;

internal sealed record MarkdownChoices(IReadOnlySet<string> Expanded)
{
    public static MarkdownChoices Collapsed { get; } = new(new HashSet<string>());

    public bool Reasoning { get; init; }
}

internal static class TranscriptMarkdown
{
    public static string Write(string title, Transcript transcript, MarkdownChoices choices)
    {
        var blocks = new List<string> { $"# {title.Trim()}" };

        foreach (var entry in transcript.Entries)
        {
            blocks.AddRange(Blocks(entry, choices));
        }

        return string.Join("\n\n", blocks) + "\n";
    }

    private static IEnumerable<string> Blocks(ITimelineEntry entry, MarkdownChoices choices) => entry switch
    {
        PromptEntry prompt => prompt.Text.Match<IEnumerable<string>>(text => [Speaker(prompt.Origin), text.Trim()], () => []),
        InterjectionEntry interjection => ["**You**", interjection.Text.Trim()],
        MessageEntry { Text.Length: > 0 } message => [message.Text.Trim()],
        ReasoningEntry { Text.Length: > 0 } reasoning when choices.Reasoning => [Quoted(reasoning.Text)],
        ToolEntry tool => Tool(tool, choices),
        CanvasEntry canvas => [$"- {canvas.Title}", Fenced(canvas.Content, Language(canvas.MediaType))],
        _ => [],
    };

    private static IEnumerable<string> Tool(ToolEntry tool, MarkdownChoices choices)
    {
        var line = $"- {tool.Title}{Ending(tool)}";

        return choices.Expanded.Contains(tool.Key) && tool.Output.Trim().Length > 0 ? [line, Fenced(tool.Output, string.Empty)] : [line];
    }

    private static string Ending(ToolEntry tool) =>
        tool.Denied ? " (denied)"
        : tool.Outcome.Match(outcome => outcome switch
        {
            ItemOutcome.Failed => " (failed)",
            ItemOutcome.Cancelled or ItemOutcome.Abandoned or ItemOutcome.Expired => " (stopped)",
            _ => string.Empty,
        }, () => string.Empty);

    private static string Speaker(AttemptOrigin origin) =>
        origin is AttemptOrigin.Initial or AttemptOrigin.Hint or AttemptOrigin.SendBack or AttemptOrigin.Device ? "**You**" : "**Avala**";

    private static string Quoted(string text) =>
        string.Join('\n', text.Trim().Split('\n').Select(line => line.Length == 0 ? ">" : $"> {line}"));

    private static string Language(string mediaType) => mediaType switch
    {
        "image/svg+xml" => "svg",
        "text/markdown" => "markdown",
        "text/vnd.mermaid" => "mermaid",
        "text/html" => "html",
        _ => string.Empty,
    };

    private static string Fenced(string text, string language)
    {
        var longest = 0;
        var run = 0;

        foreach (var character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));

        return new StringBuilder(fence).Append(language).Append('\n').Append(text.TrimEnd('\n')).Append('\n').Append(fence).ToString();
    }
}
