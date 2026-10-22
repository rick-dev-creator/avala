using System.Globalization;

namespace Avala.Workbench.Conversation;

internal static class MediaPhrases
{
    public const string Copied = "Copied.";

    public const string NotCopied = "It could not be copied.";

    public const string AddedToNote = "Added to the job's latest note.";

    public const string NotAddedToNote = "It could not be added to a note.";

    public static string Position(int index, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{index + 1} of {count}");

    public static string Markdown(string name, string caption, string path) =>
        $"![{(caption.Length > 0 ? caption : name)}]({new Uri(Path.GetFullPath(path)).AbsoluteUri})";
}
