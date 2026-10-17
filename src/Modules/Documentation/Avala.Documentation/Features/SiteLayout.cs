using System.Globalization;
using System.Text;

namespace Avala.Documentation.Features;

internal sealed record SiteFile(string Path, string Content);

internal static class SiteLayout
{
    public const string Config = "docfx.json";

    public static string RootOf(string folder)
    {
        var slash = folder.TrimEnd('/').LastIndexOf('/');

        return slash <= 0 ? "docs" : folder[..slash];
    }

    public static string FeaturesFolder(string root, string folder) =>
        folder.StartsWith(root + "/", StringComparison.Ordinal) ? folder[(root.Length + 1)..] : "features";

    public static IReadOnlyList<SiteFile> Files(string root, string folder, string name, IReadOnlyList<(string Slug, string Title)> features)
    {
        var featured = FeaturesFolder(root, folder);

        return
        [
            new($"{root}/{Config}", ConfigFile(name)),
            new($"{root}/toc.yml", RootToc(featured)),
            new($"{root}/index.md", Page(name, "overview", $"What {name} is, who it is for and where to start.")),
            new($"{root}/get-started/quickstart.md", Page("Quickstart", "quickstart", $"Run {name} for the first time.")),
            new($"{root}/concepts/architecture.md", Page("Architecture", "concept-article", $"The C4 context and containers of {name}, as built.")),
            new($"{root}/concepts/glossary.md", Page("Glossary", "concept-article", "The terms this documentation uses.")),
            new($"{root}/how-to/index.md", Page("How-to guides", "how-to", "One task each.")),
            new($"{root}/tutorials/index.md", Page("Tutorials", "tutorial", "End to end.")),
            new($"{root}/reference/configuration.md", Page("Configuration", "reference", $"Every setting {name} reads.")),
            new($"{root}/{featured}/toc.yml", FeaturesToc(features)),
            new($"{root}/architecture/decisions/index.md", Page("Architecture decisions", "concept-article", "One record per significant decision: its context, the alternatives ruled out and its consequences. The log is append-only: a later decision supersedes an earlier one.")),
            new($"{root}/architecture/decisions/toc.yml", "- name: Architecture decisions\n  href: index.md\n"),
            new($"{root}/whats-new.md", Page("What's new", "whats-new", "What each feature delivered, newest first.")),
            new($"{root}/media/.gitkeep", string.Empty),
            new($"{root}/includes/.gitkeep", string.Empty),
        ];
    }

    public static string Page(string title, string topic, string lead) =>
        $"---\ntitle: {title}\nms.topic: {topic}\n---\n\n# {title}\n\n{lead}\n";

    private static string ConfigFile(string name) =>
        string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "build": {
                "content": [ { "files": [ "**/*.{md,yml}" ], "exclude": [ "_site/**" ] } ],
                "resource": [ { "files": [ "media/**" ] } ],
                "output": "_site",
                "template": [ "default", "modern" ],
                "globalMetadata": { "_appName": "{{name}}", "_appTitle": "{{name}}", "_enableSearch": true }
              }
            }

            """);

    private static string RootToc(string featured) =>
        $"""
        - name: Overview
          href: index.md
        - name: Get started
          items:
          - name: Quickstart
            href: get-started/quickstart.md
        - name: Concepts
          items:
          - name: Architecture
            href: concepts/architecture.md
          - name: Glossary
            href: concepts/glossary.md
        - name: How-to guides
          href: how-to/index.md
        - name: Tutorials
          href: tutorials/index.md
        - name: Features
          href: {featured}/toc.yml
        - name: Architecture decisions
          href: architecture/decisions/toc.yml
        - name: Reference
          items:
          - name: Configuration
            href: reference/configuration.md
        - name: What's new
          href: whats-new.md

        """;

    private static string FeaturesToc(IReadOnlyList<(string Slug, string Title)> features)
    {
        var toc = new StringBuilder();

        foreach (var (slug, title) in features)
        {
            toc.Append(CultureInfo.InvariantCulture, $"- name: {title}\n  href: {slug}/toc.yml\n");
        }

        return toc.Length == 0 ? "[]\n" : toc.ToString();
    }
}
