using System.Text.RegularExpressions;

namespace Avala.Rendering.Highlighting;

internal static partial class SourceHighlighter
{
    public static IReadOnlyList<SourceToken> Mermaid(string source) =>
        Scan(source, MermaidTokens(), MermaidKind);

    public static IReadOnlyList<SourceToken> Html(string source)
    {
        var tokens = new List<SourceToken>();
        var at = 0;

        foreach (Match chunk in HtmlChunks().Matches(source))
        {
            Plain(tokens, source[at..chunk.Index]);
            tokens.AddRange(chunk.Groups["comment"].Success
                ? [new SourceToken(chunk.Value, TokenKind.Comment)]
                : Scan(chunk.Value, TagTokens(), TagKind));
            at = chunk.Index + chunk.Length;
        }

        Plain(tokens, source[at..]);

        return tokens;
    }

    private static TokenKind MermaidKind(Match match) =>
        match.Groups["comment"].Success ? TokenKind.Comment
        : match.Groups["text"].Success ? TokenKind.Text
        : match.Groups["arrow"].Success ? TokenKind.Operator
        : TokenKind.Keyword;

    private static TokenKind TagKind(Match match) =>
        match.Groups["name"].Success ? TokenKind.Name
        : match.Groups["text"].Success ? TokenKind.Text
        : match.Groups["attribute"].Success ? TokenKind.Attribute
        : TokenKind.Operator;

    private static List<SourceToken> Scan(string source, Regex pattern, Func<Match, TokenKind> kind)
    {
        var tokens = new List<SourceToken>();
        var at = 0;

        foreach (Match match in pattern.Matches(source))
        {
            Plain(tokens, source[at..match.Index]);
            tokens.Add(new SourceToken(match.Value, kind(match)));
            at = match.Index + match.Length;
        }

        Plain(tokens, source[at..]);

        return tokens;
    }

    private static void Plain(List<SourceToken> tokens, string text)
    {
        if (text.Length > 0)
        {
            tokens.Add(new SourceToken(text, TokenKind.Plain));
        }
    }

    [GeneratedRegex(@"(?<comment>%%[^\n]*)|(?<text>""[^""\n]*""?|\|[^|\n]*\||\[[^\]\n]*\]|\(\([^)\n]*\)\)|\([^)\n]*\)|\{[^}\n]*\})|(?<arrow>[<xo]?(?:--|==|-\.)[-.=]*(?:>>|>|x|o|\))?|->>|->|-\))|(?<keyword>\b(?:flowchart|graph|sequenceDiagram|classDiagram|stateDiagram(?:-v2)?|erDiagram|gantt|pie|journey|gitGraph|mindmap|timeline|quadrantChart|subgraph|end|participant|actor|note|loop|alt|else|opt|par|and|rect|class|style|classDef|linkStyle|click|direction|title|section|dateFormat|TD|TB|BT|RL|LR)\b)")]
    private static partial Regex MermaidTokens();

    [GeneratedRegex(@"(?<comment><!--[\s\S]*?(?:-->|$))|(?<tag><[!/?]?[A-Za-z][^>]*>?)")]
    private static partial Regex HtmlChunks();

    [GeneratedRegex(@"(?<open>^<[!/?]?)(?<name>[A-Za-z][\w:.-]*)|(?<text>""[^""]*""?|'[^']*'?)|(?<attribute>[A-Za-z_:@][\w:.-]*)|(?<close>/?>|=)")]
    private static partial Regex TagTokens();
}
