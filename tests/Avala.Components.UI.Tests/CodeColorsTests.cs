using Avala.Components.UI.Markdown;

namespace Avala.Components.UI.Tests;

public sealed class CodeColorsTests
{
    [Theory]
    [InlineData("csharp", "var name = \"Ada\"; // greet", "569CD6:var", "CE9178:\"Ada\"", "6A9955:// greet")]
    [InlineData("cs", "return 42;", "C586C0:return", "B5CEA8:42")]
    [InlineData("python", "def area(r):", "569CD6:def", "DCDCAA:area")]
    [InlineData("ts", "const n: number = 1", "569CD6:const", "4EC9B0:number", "B5CEA8:1")]
    public async Task CodeIsColoredByTheGrammarItsLanguageNamesInVisualStudioCodesDarkColorsAsync(string language, string code, params string[] expected)
    {
        var spans = (await CodeColors.LinesAsync(code, language, TestContext.Current.CancellationToken)).Single();
        var inks = spans.Where(span => !span.Ink.IsPlain).Select(span => $"{span.Ink.Dark & 0xFFFFFF:X6}:{span.Text.Trim()}").ToList();

        Assert.All(expected, ink => Assert.Contains(ink, inks));
        Assert.Equal(code, string.Concat(spans.Select(span => span.Text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("klingon")]
    public async Task CodeInAnUnknownLanguageStaysPlainAsync(string language) =>
        Assert.Equal([[new CodeSpan("a = 1", CodeInk.Plain)], [new CodeSpan("b", CodeInk.Plain)]], await CodeColors.LinesAsync("a = 1\nb", language, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ACommentOpenedOnOneLineStaysACommentOnTheNextAsync()
    {
        var lines = await CodeColors.LinesAsync("/* start\nstill */ int x;", "c", TestContext.Current.CancellationToken);

        Assert.Equal((0xFF6A9955u, 0xFF008000u), (lines[1][0].Ink.Dark, lines[1][0].Ink.Light));
        Assert.StartsWith("still", lines[1][0].Text, StringComparison.Ordinal);
    }
}
