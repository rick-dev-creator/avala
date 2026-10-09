using Avala.Rendering.Sanitizing;
using Avala.Sdk;

namespace Avala.Rendering.Tests.Sanitizing;

public sealed class SvgSanitizerTests
{
    private const string Open = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 100 100\">";

    [Fact]
    public void ADrawingWithOnlyLocalReferencesIsKeptWhole()
    {
        var svg = Open
            + "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#8DA2FB\"/></linearGradient><circle id=\"dot\" r=\"4\"/></defs>"
            + "<rect width=\"10\" height=\"10\" fill=\"url(#g)\" style=\"stroke: url('#g')\"/><use href=\"#dot\"/><use xlink:href=\"#dot\"/>"
            + "<image href=\"data:image/png;base64,iVBORw0KGgo=\"/></svg>";

        var sanitized = Sanitized(svg);

        Assert.Equal(0, sanitized.Blocked);
        Assert.Contains("url(#g)", sanitized.Markup, StringComparison.Ordinal);
        Assert.Equal(2, Count(sanitized.Markup, "#dot"));
        Assert.Contains("data:image/png;base64", sanitized.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptsEventHandlersAndEmbeddedDocumentsAreRemoved()
    {
        var svg = Open
            + "<script>fetch('https://example.com')</script>"
            + "<rect width=\"10\" height=\"10\" onclick=\"alert(1)\" onload=\"steal()\"/>"
            + "<foreignObject><iframe src=\"https://example.com\"/></foreignObject>"
            + "<a href=\"#top\"><set attributeName=\"href\" to=\"javascript:alert(1)\"/><text>top</text></a>"
            + "<?xml-stylesheet href=\"https://example.com/style.css\"?></svg>";

        var sanitized = Sanitized(svg);

        Assert.Equal(6, sanitized.Blocked);
        Assert.All(["script", "onclick", "onload", "foreignObject", "iframe", "javascript", "xml-stylesheet"], banned =>
            Assert.DoesNotContain(banned, sanitized.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("<text>top</text>", sanitized.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalReferencesAreBlockedWhereverTheyAppear()
    {
        var svg = Open
            + "<style>@import url(https://example.com/a.css); rect { fill: url(https://example.com/p.svg#p) }</style>"
            + "<image href=\"https://example.com/tracker.png\"/><image xlink:href=\"file:///etc/passwd\"/>"
            + "<use href=\"sprites.svg#icon\"/><a href=\"https://example.com\"><text>out</text></a>"
            + "<rect style=\"fill: url( 'http://example.com/x' )\"/></svg>";

        var sanitized = Sanitized(svg);

        Assert.Equal(7, sanitized.Blocked);
        Assert.All(["example.com", "file:", "sprites.svg", "@import"], banned =>
            Assert.DoesNotContain(banned, sanitized.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"10\"")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><g></svg>")]
    [InlineData("<html><body/></html>")]
    [InlineData("flowchart LR")]
    [InlineData("")]
    public void ContentThatIsNotACompleteSvgDocumentIsRejected(string content) =>
        Assert.True(SvgSanitizer.Sanitize(content).IsNone);

    [Theory]
    [InlineData("<!DOCTYPE svg [<!ENTITY secret SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&secret;</text></svg>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&b;</text></svg>")]
    public void EntitiesAreNeverExpanded(string content) =>
        Assert.True(SvgSanitizer.Sanitize(content).IsNone);

    [Fact]
    public void ADocumentTypeWithoutEntitiesIsIgnored()
    {
        var sanitized = Sanitized("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\"/></svg>");

        Assert.DoesNotContain("DOCTYPE", sanitized.Markup, StringComparison.Ordinal);
    }

    private static SanitizedSvg Sanitized(string svg) =>
        SvgSanitizer.Sanitize(svg).Match(value => value, () => throw new Xunit.Sdk.XunitException("The drawing was rejected."));

    private static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;
}
