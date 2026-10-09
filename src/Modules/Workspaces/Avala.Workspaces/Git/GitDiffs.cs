using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal static partial class GitDiffs
{
    public static IReadOnlyList<FileChange> Changes(string statuses, string counts)
    {
        var tokens = statuses.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var numbers = counts.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.Split('\t', 3))
            .Where(fields => fields.Length == 3)
            .ToDictionary(fields => fields[2], fields => (Added: Count(fields[0]), Removed: Count(fields[1])), StringComparer.Ordinal);

        return
        [
            .. tokens.Chunk(2)
                .Where(pair => pair.Length == 2)
                .Select(pair => (Kind: Kind(pair[0]), Path: pair[1]))
                .Select(change => numbers.TryGetValue(change.Path, out var counted)
                    ? new FileChange(change.Path, change.Kind, counted.Added, counted.Removed)
                    : new FileChange(change.Path, change.Kind, Option<int>.None, Option<int>.None)),
        ];
    }

    public static Option<FileDiff> OfFile(string path, string output)
    {
        if (output.Length == 0)
        {
            return Option<FileDiff>.None;
        }

        var lines = output.Split('\n');

        if (lines.Any(line => line.StartsWith("Binary files ", StringComparison.Ordinal)))
        {
            return new FileDiff(path, Binary: true, []);
        }

        var hunks = new List<(Match Header, List<DiffLine> Lines)>();

        foreach (var line in lines.SkipWhile(line => !line.StartsWith("@@", StringComparison.Ordinal)))
        {
            if (HunkHeader().Match(line) is { Success: true } header)
            {
                hunks.Add((header, []));
            }
            else
            {
                hunks[^1].Lines.AddRange(Line(line).Match<DiffLine[]>(parsed => [parsed], () => []));
            }
        }

        return new FileDiff(
            path,
            Binary: false,
            [
                .. hunks.Select(hunk => new DiffHunk(
                    Number(hunk.Header, "oldStart"),
                    Lines(hunk.Header, "oldLines"),
                    Number(hunk.Header, "newStart"),
                    Lines(hunk.Header, "newLines"),
                    hunk.Header.Groups["section"].Value,
                    hunk.Lines)),
            ]);
    }

    public static Option<string> CheckoutOf(string worktrees, string branchLine)
    {
        var current = Option<string>.None;

        foreach (var field in worktrees.Split('\0'))
        {
            if (field.StartsWith("worktree ", StringComparison.Ordinal))
            {
                current = field["worktree ".Length..];
            }
            else if (field == branchLine)
            {
                return current;
            }
        }

        return Option<string>.None;
    }

    private static Option<DiffLine> Line(string line) => line switch
    {
        [' ', .. var text] => new DiffLine(DiffLineKind.Context, text),
        ['+', .. var text] => new DiffLine(DiffLineKind.Added, text),
        ['-', .. var text] => new DiffLine(DiffLineKind.Removed, text),
        _ => Option<DiffLine>.None,
    };

    private static ChangeKind Kind(string status) => status switch
    {
        "A" => ChangeKind.Added,
        "D" => ChangeKind.Deleted,
        _ => ChangeKind.Modified,
    };

    private static Option<int> Count(string field) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : Option<int>.None;

    private static int Number(Match header, string group) => int.Parse(header.Groups[group].Value, CultureInfo.InvariantCulture);

    private static int Lines(Match header, string group) =>
        header.Groups[group].Success ? int.Parse(header.Groups[group].Value, CultureInfo.InvariantCulture) : 1;

    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(?:,(?<oldLines>\d+))? \+(?<newStart>\d+)(?:,(?<newLines>\d+))? @@ ?(?<section>.*)$")]
    private static partial Regex HunkHeader();
}
