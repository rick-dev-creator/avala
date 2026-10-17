using System.Text;
using System.Text.RegularExpressions;
using Avala.Documentation.Contracts;

namespace Avala.Documentation.Features;

internal static partial class VersionReader
{
    public static VersionDocument Read(string markdown)
    {
        var reading = new Reading();

        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            reading.Take(line);
        }

        return reading.Document;
    }

    private sealed class Reading
    {
        private readonly StringBuilder lead = new();
        private readonly List<C4Diagram> diagrams = [];
        private readonly List<string> unread = [];
        private readonly List<DocSection> sections = [];
        private (string Anchor, string Title, StringBuilder Text)? section;
        private StringBuilder? fence;
        private string title = string.Empty;
        private bool leading = true;

        public VersionDocument Document
        {
            get
            {
                Close();

                return new VersionDocument(title, Paragraph(lead.ToString()), diagrams, sections) { Unread = unread };
            }
        }

        public void Take(string line)
        {
            if (fence is not null)
            {
                Fenced(line);
            }
            else if (line.Trim() == "```mermaid")
            {
                fence = new StringBuilder();
                leading = false;
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal) && title.Length == 0)
            {
                title = line[2..].Trim();
            }
            else if (Heading().Match(line) is { Success: true } heading)
            {
                Close();
                var text = heading.Groups[1].Value.Trim();
                section = (heading.Groups[2].Success ? heading.Groups[2].Value : FeatureNames.Slug(text), text, new StringBuilder());
                leading = false;
            }
            else
            {
                Text(line);
            }
        }

        private void Fenced(string line)
        {
            if (line.Trim() != "```")
            {
                fence!.Append(line).Append('\n');

                return;
            }

            var source = fence!.ToString();
            MermaidC4.Read(source).Match(diagram => { diagrams.Add(diagram); return true; }, () => { unread.Add(source); return false; });
            fence = null;
        }

        private void Text(string line)
        {
            if (section is { } open)
            {
                open.Text.Append(line).Append('\n');
            }
            else if (leading && title.Length > 0)
            {
                lead.Append(line).Append('\n');
            }
        }

        private void Close()
        {
            if (section is { } done)
            {
                sections.Add(new DocSection(done.Anchor, done.Title, done.Text.ToString().Trim()));
                section = null;
            }
        }
    }

    private static string Paragraph(string text) =>
        text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    [GeneratedRegex(@"^#{2,3}\s+(.+?)(?:\s*\{#([A-Za-z0-9_\-]+)\})?\s*$")]
    private static partial Regex Heading();
}
