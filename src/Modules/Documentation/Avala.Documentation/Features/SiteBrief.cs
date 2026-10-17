namespace Avala.Documentation.Features;

internal static class SiteBrief
{
    public static string Fill(string root) => $$"""
        Fill this repository's documentation site in {{root}}, which Avala just laid out the way Microsoft Learn structures documentation.

        Read the repository, then replace the placeholder text of these pages with what is true of it, keeping each page's front matter and its ms.topic:

        - {{root}}/index.md: what it is, who it is for and where to start.
        - {{root}}/get-started/quickstart.md: how to build and run it for the first time, with the real commands.
        - {{root}}/concepts/architecture.md: its C4 context and containers as built, as Mermaid C4 diagrams in mermaid code blocks.
        - {{root}}/reference/configuration.md: every setting it reads, where and with which default.

        Then, for each feature already documented under {{root}}, write an architecture decision record in {{root}}/architecture/decisions/ for each decision that shaped its structure, a key quality attribute or something hard to reverse, numbered 0001-<slug>.md onward, with Status, Context, Decision, Alternatives and Consequences, and list them in its toc.yml.

        Change nothing outside {{root}}.
        """;
}
