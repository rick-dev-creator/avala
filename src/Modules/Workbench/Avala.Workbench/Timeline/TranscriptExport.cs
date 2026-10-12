using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal sealed class TranscriptExport(IClipboard clipboard, IFileSaver saver)
{
    public const string Title = "Export the conversation";

    public static string FileName(string title)
    {
        var words = new string([.. title.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(8);
        var name = string.Join('-', words);

        return $"{(name.Length == 0 ? "conversation" : name)}.md";
    }

    public async Task<bool> CopyAsync(string title, Transcript transcript, MarkdownChoices choices, CancellationToken cancellationToken) =>
        await clipboard.CopyAsync(TranscriptMarkdown.Write(title, transcript, choices), cancellationToken);

    public async Task<Result<string, FileSaveError>> ExportAsync(string title, Transcript transcript, MarkdownChoices choices, CancellationToken cancellationToken) =>
        await saver.SaveAsync(Title, FileName(title), TranscriptMarkdown.Write(title, transcript, choices), cancellationToken);
}
