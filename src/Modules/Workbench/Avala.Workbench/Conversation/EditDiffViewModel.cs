using System.Globalization;
using Avala.Workbench.Review;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Conversation;

internal interface IEditDiffViewModel
{
    string Header { get; }

    IReadOnlyList<HunkLine> Lines { get; }

    string Diff { get; }

    string Language { get; }
}

internal sealed class EditDiffViewModel : IEditDiffViewModel
{
    public const int ShownLines = 400;

    private EditDiffViewModel(string header, IReadOnlyList<HunkLine> lines, string language)
    {
        Header = header;
        Lines = lines;
        Language = language;
    }

    public string Header { get; }

    public IReadOnlyList<HunkLine> Lines { get; }

    public string Diff => DiffLanguage.Text(Lines);

    public string Language { get; }

    public static EditDiffViewModel Of(string path, string input)
    {
        var lines = input.TrimEnd('\n').Split('\n');
        var replaces = lines.All(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("+ ", StringComparison.Ordinal) || line is "-" or "+");
        var shown = lines.Take(ShownLines).Select(line => Line(line, replaces)).ToList();

        if (lines.Length > ShownLines)
        {
            shown.Add(new HunkLine(ConversationPhrases.MoreLines(lines.Length - ShownLines), DiffLineKind.Context));
        }

        var added = lines.Count(line => !replaces || line.StartsWith('+'));
        var removed = replaces ? lines.Count(line => line.StartsWith('-')) : 0;

        return new EditDiffViewModel(string.Create(CultureInfo.InvariantCulture, $"{path}  +{added} -{removed}").TrimStart(), shown, DiffLanguage.Of(path));
    }

    private static HunkLine Line(string line, bool replaces) =>
        !replaces ? new HunkLine($"+{line}", DiffLineKind.Added)
        : line.StartsWith('-') ? new HunkLine($"-{line[Math.Min(2, line.Length)..]}", DiffLineKind.Removed)
        : new HunkLine($"+{line[Math.Min(2, line.Length)..]}", DiffLineKind.Added);
}
