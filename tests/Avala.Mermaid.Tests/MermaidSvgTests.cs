using Avala.Mermaid.Translating;
using Avala.Sdk;

namespace Avala.Mermaid.Tests;

public sealed class MermaidSvgTests
{
    private const string Flow = "flowchart LR\n  Submitted --> Running\n  Running -->|checks pass| Done\n  subgraph Review\n    Done\n  end\n";

    [Fact]
    public void ADiagramBecomesAnSvgWithEveryCssVariableAndColorMixResolved()
    {
        var svg = Drawn(Flow);

        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("var(", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("color-mix(", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("--", svg.Replace("<!--", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.All(["Submitted", "Running", "Done", "checks pass"], label => Assert.Contains($">{label}<", svg, StringComparison.Ordinal));
    }

    [Fact]
    public void TheDiagramTakesThePalettesColors()
    {
        var light = Drawn(Flow, new MermaidPalette("#FFFFFF", "#1D1D1F", "#4458CC", "#5E5E66"));
        var dark = Drawn(Flow, MermaidPalette.Dark);

        Assert.Contains("fill=\"#1D1D1F\"", light, StringComparison.Ordinal);
        Assert.Contains("fill=\"#EDEDEF\"", dark, StringComparison.Ordinal);
        Assert.DoesNotContain("#EDEDEF", light, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("color-mix(in srgb, #ffffff 50%, #000000)", "#808080")]
    [InlineData("color-mix(in srgb, #ff0000 30%, #0000ff)", "#4D00B3")]
    [InlineData("color-mix(in srgb, #ff0000, #0000ff)", "#800080")]
    [InlineData("color-mix(in srgb, #336699 40%, transparent)", "#33669966")]
    [InlineData("color-mix(in srgb, color-mix(in srgb, #ffffff 50%, black) 100%, #123456)", "#808080")]
    [InlineData("var(--fg)", "#EDEDEF")]
    [InlineData("var(--missing, var(--fg))", "#EDEDEF")]
    [InlineData("var(--line)", "#959597")]
    [InlineData("var(--fs-xs)", "12")]
    public void CssValuesResolveToPlainColorsAndPixels(string value, string resolved)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--fg"] = "#EDEDEF",
            ["--bg"] = "#101114",
            ["--line"] = "color-mix(in srgb, var(--fg) 60%, var(--bg))",
            ["--fs-xs"] = "0.75rem",
        };

        Assert.Equal(resolved, CssVariables.Resolve(value, variables));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not a diagram")]
    public void TextThatIsNotADiagramDrawsNothing(string source) =>
        Assert.Equal(Option<string>.None, MermaidSvg.Draw(source, MermaidPalette.Dark));

    [Fact]
    public void ScriptsAndExternalLinksNeverReachTheSvg()
    {
        var svg = Drawn("flowchart LR\n  A --> B\n  click A href \"javascript:alert(1)\"\n  click B href \"https://example.com\"\n");

        Assert.DoesNotContain("javascript:", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://example.com", svg, StringComparison.Ordinal);
    }

    private static string Drawn(string source, MermaidPalette? palette = null) =>
        MermaidSvg.Draw(source, palette ?? MermaidPalette.Dark).Match(svg => svg, () => throw new InvalidOperationException("The diagram was not drawn."));
}
