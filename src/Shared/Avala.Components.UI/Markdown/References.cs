using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Avala.Components.UI.Markdown;

public static partial class References
{
    public const string Scheme = "avala-ref";

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "csproj", "axaml", "xaml", "fs", "vb", "ts", "tsx", "js", "jsx", "mjs", "cjs", "py", "go", "rs", "java", "kt", "swift", "rb", "php",
        "c", "h", "cpp", "hpp", "cc", "m", "scala", "sql", "sh", "ps1", "md", "json", "yml", "yaml", "toml", "xml", "html", "css", "scss", "vue",
        "svelte", "lua", "dart", "ex", "exs", "zig", "props", "targets", "sln", "slnx", "lock", "txt", "cfg", "ini", "env", "gradle", "proto",
    };

    public static string File(string path, int line) => line > 0 ? $"{Scheme}:file/{Uri.EscapeDataString(path)}#L{line}" : $"{Scheme}:file/{Uri.EscapeDataString(path)}";

    public static string Commit(string sha) => $"{Scheme}:commit/{sha}";

    public static string Issue(int number) => $"{Scheme}:issue/{number}";

    public static void Link(MarkdownDocument document)
    {
        foreach (var code in document.Descendants<CodeInline>().Where(code => code.Parent is not LinkInline).ToList())
        {
            if (Target(code.Content.Trim()) is { } url)
            {
                var link = new LinkInline(url, string.Empty);
                code.ReplaceBy(link);
                link.AppendChild(code);
            }
        }

        foreach (var literal in document.Descendants<LiteralInline>().Where(literal => !InsideLink(literal)).ToList())
        {
            Split(literal);
        }
    }

    private static string? Target(string text)
    {
        if (PathPattern().Match(text) is { Success: true } path && Linkable(path))
        {
            return File(path.Groups["path"].Value, path.Groups["line"].Success ? int.Parse(path.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0);
        }

        if (CommitPattern().IsMatch(text) && Hashlike(text))
        {
            return Commit(text);
        }

        return null;
    }

    private static bool Linkable(Match path) =>
        path.Groups["path"].Value.Contains('/', StringComparison.Ordinal) || CodeExtensions.Contains(path.Groups["extension"].Value);

    private static bool Hashlike(string text) => text.Any(char.IsDigit) && text.Any(character => character is >= 'a' and <= 'f');

    private static bool InsideLink(Inline inline)
    {
        for (var parent = inline.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is LinkInline)
            {
                return true;
            }
        }

        return false;
    }

    private static void Split(LiteralInline literal)
    {
        var text = literal.Content.ToString();
        var found = Mentions().Matches(text).Where(match => Url(match) is not null).ToList();

        if (found.Count == 0)
        {
            return;
        }

        Inline at = literal;
        var start = 0;
        var pieces = new List<Inline>();

        foreach (var match in found)
        {
            if (match.Index > start)
            {
                pieces.Add(new LiteralInline(text[start..match.Index]));
            }

            var link = new LinkInline(Url(match)!, string.Empty);
            link.AppendChild(new LiteralInline(match.Value));
            pieces.Add(link);
            start = match.Index + match.Length;
        }

        if (start < text.Length)
        {
            pieces.Add(new LiteralInline(text[start..]));
        }

        foreach (var piece in pieces)
        {
            at.InsertAfter(piece);
            at = piece;
        }

        literal.Remove();
    }

    private static string? Url(Match match) =>
        match.Groups["issue"].Success ? Issue(int.Parse(match.Groups["issue"].Value, System.Globalization.CultureInfo.InvariantCulture)) : Target(match.Value);

    [GeneratedRegex(@"^(?<path>(?:\.{0,2}/)?(?:[\w@.-]+/)*[\w@-][\w@.-]*\.(?<extension>[A-Za-z0-9]{1,10}))(?::(?<line>\d{1,7})(?::\d{1,5})?)?$")]
    private static partial Regex PathPattern();

    [GeneratedRegex("^[0-9a-f]{7,40}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"(?<![\w&/#])#(?<issue>\d{1,7})\b|(?<![\w/.@-])(?:\.{0,2}/)?(?:[\w@.-]+/)*[\w@-][\w@.-]*\.[A-Za-z0-9]{1,10}(?::\d{1,7}(?::\d{1,5})?)?(?![\w/])|(?<![\w])[0-9a-f]{7,40}(?![\w])")]
    private static partial Regex Mentions();
}
