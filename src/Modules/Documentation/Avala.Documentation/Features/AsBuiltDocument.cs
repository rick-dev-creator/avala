using System.Globalization;
using System.Text;
using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Features;

internal sealed record CheckOutcome(string Name, string Status);

internal sealed record ChangedFile(string Path, Option<int> Added, Option<int> Removed);

internal sealed record BuildEvidence(int Attempt, IReadOnlyList<CheckOutcome> Checks, IReadOnlyList<ChangedFile> Files);

internal static class AsBuiltDocument
{
    public static string Compose(Feature feature, int built, Option<string> previous, BuildEvidence evidence)
    {
        var document = new StringBuilder();
        document.Append(CultureInfo.InvariantCulture, $"# {feature.Title}\n\n");
        document.Append(CultureInfo.InvariantCulture, $"As built: what attempt {evidence.Attempt} of the job delivered for review, from version {built}. Written by Avala from the job's evidence.\n\n");
        previous.Match(text => document.Append(Body(text)).Append("\n\n"), () => document);
        document.Append("## Evidence\n\n");
        document.Append(evidence.Checks.Count == 0
            ? "- Checks: none declared\n"
            : "- Checks: " + string.Join(", ", evidence.Checks.Select(check => $"{check.Name} {check.Status.ToLowerInvariant()}")) + "\n");
        document.Append(CultureInfo.InvariantCulture, $"- Files changed: {evidence.Files.Count}, +{evidence.Files.Sum(file => file.Added.Match(lines => lines, () => 0))} −{evidence.Files.Sum(file => file.Removed.Match(lines => lines, () => 0))}\n");

        foreach (var file in evidence.Files)
        {
            document.Append(CultureInfo.InvariantCulture, $"  - `{file.Path}`{Lines(file)}\n");
        }

        return document.ToString();
    }

    private static string Body(string previous)
    {
        var lines = previous.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var heading = Array.FindIndex(lines, line => line.StartsWith("# ", StringComparison.Ordinal));

        var body = heading < 0 ? lines : lines[(heading + 1)..];
        var kept = Without(Without(body, "## Changes from"), "## Evidence");

        return string.Join('\n', kept).Trim();
    }

    private static string[] Without(string[] body, string section)
    {
        var start = Array.FindIndex(body, line => line.StartsWith(section, StringComparison.Ordinal));
        var end = start < 0 ? -1 : Array.FindIndex(body, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));

        return start < 0 ? body : Without([.. body[..start], .. end < 0 ? [] : body[end..]], section);
    }

    private static string Lines(ChangedFile file) =>
        file.Added.IsSome || file.Removed.IsSome
            ? string.Create(CultureInfo.InvariantCulture, $" +{file.Added.Match(lines => lines, () => 0)} −{file.Removed.Match(lines => lines, () => 0)}")
            : string.Empty;
}
