using System.Globalization;
using System.Text.RegularExpressions;

namespace Avala.Documentation.Features;

internal sealed record DecisionRecord(int Number, string Title, string File)
{
    public string Name => string.Create(CultureInfo.InvariantCulture, $"ADR-{Number:0000}");
}

internal static partial class SiteRecords
{
    public static string Decisions(string root) => $"{root}/architecture/decisions";

    public static DecisionRecord Next(IEnumerable<string> files, string root, string decision)
    {
        var folder = Decisions(root) + "/";
        var numbers = files.Where(file => file.StartsWith(folder, StringComparison.Ordinal))
            .Select(file => Numbered().Match(file[folder.Length..]))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        var number = numbers.Count == 0 ? 1 : numbers.Max() + 1;
        var title = FeatureFolder.TitleIn(decision).Match(found => found, () => "Decision");

        return new DecisionRecord(number, title, string.Create(CultureInfo.InvariantCulture, $"{number:0000}-{FeatureNames.Slug(title)}.md"));
    }

    public static string Up(string from, string root) =>
        string.Concat(Enumerable.Repeat("../", from[(root.Length + 1)..].Split('/').Length));

    public static string Shaped(string decision, string feature, int version, string link) =>
        decision.TrimEnd() + string.Create(CultureInfo.InvariantCulture, $"\n\n## Versions it shaped\n\n- [{feature} v{version}]({link})\n");

    public static string Explained(string document, DecisionRecord record, string link) =>
        document.TrimEnd() + $"\n\n## Decisions {{#decisions}}\n\n- [{record.Name}: {record.Title}]({link})\n";

    public static string WhatsNew(string existing, string entry)
    {
        var lines = existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var heading = lines.FindIndex(line => line.StartsWith("# ", StringComparison.Ordinal));
        var lead = heading < 0 ? -1 : lines.FindIndex(heading + 1, line => line.Trim().Length > 0);
        var at = lead < 0 ? lines.Count : lead + 1;
        lines.InsertRange(at, ["", .. entry.TrimEnd().Split('\n')]);

        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    [GeneratedRegex(@"^(\d{4})-.+\.md$")]
    private static partial Regex Numbered();
}
