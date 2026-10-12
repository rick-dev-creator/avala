using Avala.Components.UI.Markdown;

namespace Avala.Components.UI.Tests;

public sealed class CodeColorsTests
{
    [Theory]
    [InlineData("csharp", "var name = \"Ada\"; // greet", "keyword:var", "string:\"Ada\"", "comment:// greet")]
    [InlineData("cs", "return 42;", "keyword:return", "number:42")]
    [InlineData("python", "def area(r):", "keyword:def", "function:area")]
    [InlineData("ts", "const n: number = 1", "keyword:const", "type:number", "number:1")]
    public async Task CodeIsColoredByTheGrammarItsLanguageNamesAsync(string language, string code, params string[] expected)
    {
        var spans = (await CodeColors.LinesAsync(code, language, TestContext.Current.CancellationToken)).Single();
        var roles = spans.Where(span => span.Role.Length > 0).Select(span => $"{span.Role}:{span.Text.Trim()}").ToList();

        Assert.All(expected, role => Assert.Contains(role, roles));
        Assert.Equal(code, string.Concat(spans.Select(span => span.Text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("klingon")]
    public async Task CodeInAnUnknownLanguageStaysPlainAsync(string language) =>
        Assert.Equal([[new CodeSpan("a = 1", string.Empty)], [new CodeSpan("b", string.Empty)]], await CodeColors.LinesAsync("a = 1\nb", language, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ACommentOpenedOnOneLineStaysACommentOnTheNextAsync()
    {
        var lines = await CodeColors.LinesAsync("/* start\nstill */ int x;", "c", TestContext.Current.CancellationToken);

        Assert.Equal("comment", lines[1][0].Role);
        Assert.StartsWith("still", lines[1][0].Text, StringComparison.Ordinal);
    }
}
