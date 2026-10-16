using System.Text.RegularExpressions;

namespace Avala.Workbench.Conversation;

internal sealed record FoldedOutput(string Shown, int Hidden, IReadOnlyList<string> Problems);

internal static partial class OutputFold
{
    public const int Kept = 20;

    public const int MostProblems = 8;

    public static FoldedOutput Of(string output, bool finished, bool failed, bool all)
    {
        var lines = output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var folds = finished && !all && lines.Length > Kept;
        var problems = failed ? Problems(lines) : [];

        return new FoldedOutput(folds ? string.Join('\n', lines[^Kept..]) : output, folds ? lines.Length - Kept : 0, problems);
    }

    private static IReadOnlyList<string> Problems(string[] lines) =>
        [.. lines.Select(line => line.Trim()).Where(line => Problem().IsMatch(line) && !Clean().IsMatch(line)).Distinct(StringComparer.Ordinal).Take(MostProblems)];

    [GeneratedRegex(@"\b(error|errors|failed|failure|fail|panic|exception|fatal)\b|exit (code|status) [1-9]", RegexOptions.IgnoreCase)]
    private static partial Regex Problem();

    [GeneratedRegex(@"\b(0|no) (errors?|failures?|failed)\b|\b(errors?|failures?|failed):\s*0\b", RegexOptions.IgnoreCase)]
    private static partial Regex Clean();
}
