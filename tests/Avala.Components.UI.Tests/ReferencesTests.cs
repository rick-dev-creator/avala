using Avala.Components.UI.Markdown;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Avala.Components.UI.Tests;

public sealed class ReferencesTests
{
    [Theory]
    [InlineData("Fixed in `src/Invoice.cs:42`.", "avala-ref:file/src%2FInvoice.cs#L42")]
    [InlineData("The rounding lives in src/money/round.ts now.", "avala-ref:file/src%2Fmoney%2Fround.ts")]
    [InlineData("Program.cs starts it.", "avala-ref:file/Program.cs")]
    [InlineData("It landed in 01a13a5 on main.", "avala-ref:commit/01a13a5")]
    [InlineData("`01a13a5f`", "avala-ref:commit/01a13a5f")]
    [InlineData("This closes #12.", "avala-ref:issue/12")]
    public void AReferenceInAReplyBecomesALinkToIt(string markdown, string link) =>
        Assert.Equal([link], Links(markdown));

    [Theory]
    [InlineData("For example, e.g. this.")]
    [InlineData("Version 1.2 and 1234567 and deadbeef.")]
    [InlineData("A&#12; entity and a#12 tag.")]
    [InlineData("```\nsrc/Invoice.cs:42\n```")]
    public void TextThatOnlyLooksLikeAReferenceStaysText(string markdown) =>
        Assert.Empty(Links(markdown));

    [Fact]
    public void ALinkTheAgentWroteIsKeptAsItIs() =>
        Assert.Equal(["https://example.com/src/app.ts"], Links("See [src/app.ts](https://example.com/src/app.ts) and https://example.com/src/app.ts").Distinct());

    [Fact]
    public void TheTextAroundAReferenceIsKeptInOrder()
    {
        var document = Markdig.Markdown.Parse("Look at src/a.ts then #3 please.", OfflineMarkdown.WithReferences);

        Assert.Equal("Look at src/a.ts then #3 please.", string.Concat(document.Descendants<LiteralInline>().Select(literal => literal.Content.ToString())));
    }

    private static List<string> Links(string markdown) =>
        [.. Markdig.Markdown.Parse(markdown, OfflineMarkdown.WithReferences).Descendants<LinkInline>().Select(link => link.Url ?? string.Empty)];
}
