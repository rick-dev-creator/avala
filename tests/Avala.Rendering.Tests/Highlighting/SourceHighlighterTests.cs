using Avala.Rendering.Highlighting;

namespace Avala.Rendering.Tests.Highlighting;

public sealed class SourceHighlighterTests
{
    private const string Flowchart = "%% rounding\nflowchart LR\n  A[Amount ¥1,000] -->|ToMinor| B(Round)\n  B -.-> C{Done}";

    private const string Page = "<!-- report -->\n<div class=\"total\" data-x='1'>¥1,235</div>";

    [Fact]
    public void MermaidKeywordsArrowsLabelsAndCommentsAreToldApart()
    {
        var tokens = SourceHighlighter.Mermaid(Flowchart);

        Assert.Equal(["flowchart", "LR"], Of(tokens, TokenKind.Keyword));
        Assert.Equal(["-->", "-.->"], Of(tokens, TokenKind.Operator));
        Assert.Equal(["[Amount ¥1,000]", "|ToMinor|", "(Round)", "{Done}"], Of(tokens, TokenKind.Text));
        Assert.Equal(["%% rounding"], Of(tokens, TokenKind.Comment));
    }

    [Fact]
    public void HtmlTagsAttributesValuesAndCommentsAreToldApart()
    {
        var tokens = SourceHighlighter.Html(Page);

        Assert.Equal(["<div", "</div"], Of(tokens, TokenKind.Name));
        Assert.Equal(["class", "data-x"], Of(tokens, TokenKind.Attribute));
        Assert.Equal(["\"total\"", "'1'"], Of(tokens, TokenKind.Text));
        Assert.Equal(["<!-- report -->"], Of(tokens, TokenKind.Comment));
        Assert.Contains(new SourceToken("¥1,235", TokenKind.Plain), tokens);
    }

    [Theory]
    [InlineData(Flowchart)]
    [InlineData("sequenceDiagram\n  Alice->>Bob: \"hi\n")]
    [InlineData("graph TD\n  A[unclosed")]
    [InlineData("")]
    public void HighlightingMermaidNeverChangesItsText(string source) =>
        Assert.Equal(source, string.Concat(SourceHighlighter.Mermaid(source).Select(token => token.Text)));

    [Theory]
    [InlineData(Page)]
    [InlineData("<div class=\"unclosed")]
    [InlineData("<!-- unclosed comment")]
    [InlineData("a < b && c > d")]
    public void HighlightingHtmlNeverChangesItsText(string source) =>
        Assert.Equal(source, string.Concat(SourceHighlighter.Html(source).Select(token => token.Text)));

    private static string[] Of(IReadOnlyList<SourceToken> tokens, TokenKind kind) =>
        [.. tokens.Where(token => token.Kind == kind).Select(token => token.Text)];
}
