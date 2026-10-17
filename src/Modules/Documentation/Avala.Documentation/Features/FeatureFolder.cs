using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Features;

internal sealed record VersionFile(int Number, VersionKind Kind, string File);

internal sealed partial record FeatureFolder(string Root, Feature Feature)
{
    public string Path => $"{Root}/{Feature.Slug}";

    public string Readme => $"{Path}/README.md";

    public string Toc => $"{Path}/toc.yml";

    public string Plan(int number) => string.Create(CultureInfo.InvariantCulture, $"{Path}/plans/v{number}.md");

    public string Version(int number, VersionKind kind) => $"{Path}/{FileOf(number, kind)}";

    public static string FileOf(int number, VersionKind kind) => string.Create(CultureInfo.InvariantCulture, $"v{number}-{Name(kind)}.md");

    public static string Name(VersionKind kind) => kind switch
    {
        VersionKind.Proposed => "proposed",
        VersionKind.Deviation => "deviation",
        _ => "as-built",
    };

    public static string Label(VersionKind kind) => kind switch
    {
        VersionKind.Proposed => "Proposed",
        VersionKind.Deviation => "Deviation",
        _ => "As built",
    };

    public IReadOnlyList<VersionFile> VersionsIn(IEnumerable<string> files) =>
        [.. files
            .Where(file => file.StartsWith(Path + "/", StringComparison.Ordinal) && !file[(Path.Length + 1)..].Contains('/'))
            .Select(file => VersionPattern().Match(file[(Path.Length + 1)..]))
            .Where(match => match.Success)
            .Select(match => new VersionFile(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), KindOf(match.Groups[2].Value), match.Value))
            .OrderBy(version => version.Number)];

    public static Option<string> TitleIn(string readme) =>
        readme.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal)) is { } heading
            ? heading[2..].Trim()
            : Option<string>.None;

    public string Index(IReadOnlyList<VersionFile> versions)
    {
        var index = new StringBuilder();
        index.Append(CultureInfo.InvariantCulture, $"# {Feature.Title}\n\n");
        index.Append("Written by Avala as the feature is built. Each version is a moment of the feature, with its C4 diagram.\n\n");
        index.Append("| Version | Kind | Document |\n| --- | --- | --- |\n");

        foreach (var version in versions)
        {
            index.Append(CultureInfo.InvariantCulture, $"| v{version.Number} | {Label(version.Kind)} | [{version.File}]({version.File}) |\n");
        }

        return index.ToString();
    }

    public static string TableOfContents(IReadOnlyList<VersionFile> versions)
    {
        var toc = new StringBuilder("- name: Overview\n  href: README.md\n");

        foreach (var version in versions)
        {
            toc.Append(CultureInfo.InvariantCulture, $"- name: v{version.Number} · {Label(version.Kind)}\n  href: {version.File}\n");
        }

        return toc.ToString();
    }

    public static string PlanDocument(int number, IReadOnlyList<string> steps) =>
        string.Create(CultureInfo.InvariantCulture, $"# Plan v{number}\n\n") + string.Concat(steps.Select((step, index) => string.Create(CultureInfo.InvariantCulture, $"{index + 1}. {step}\n")));

    private static VersionKind KindOf(string name) => name switch
    {
        "proposed" => VersionKind.Proposed,
        "deviation" => VersionKind.Deviation,
        _ => VersionKind.AsBuilt,
    };

    [GeneratedRegex(@"^v(\d{1,6})-(proposed|deviation|as-built)\.md$")]
    private static partial Regex VersionPattern();
}
