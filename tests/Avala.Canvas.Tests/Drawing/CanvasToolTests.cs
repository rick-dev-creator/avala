using System.Text.Json;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Canvas.Tests.Drawing;

public sealed class CanvasToolTests
{
    [Fact]
    public async Task TheCanvasToolAsksForATitleOneOfTheOfferedMediaTypesAndTheContentAsync()
    {
        await using var composition = Compose(Sketch.Svg, Sketch.Markdown);
        var tool = Assert.Single(composition.All<HarnessTool>());
        using var schema = JsonDocument.Parse(tool.InputSchema);

        Assert.Equal((ToolSurface.Canvas, "canvas"), (tool.Surface, tool.Name));
        Assert.Equal(
            ["title", "mediaType", "content"],
            schema.RootElement.GetProperty("required").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(["image/svg+xml", "text/markdown"], OfferedIn(schema));
    }

    [Fact]
    public async Task TheCanvasToolOffersOnlyTheFormatsRenderersDeclaredAsync()
    {
        await using var composition = Compose(Sketch.Markdown);
        using var schema = JsonDocument.Parse(Assert.Single(composition.All<HarnessTool>()).InputSchema);

        Assert.Equal(["text/markdown"], OfferedIn(schema));
    }

    [Fact]
    public async Task TheOfferFollowsEachFormatsDeclaredOrderWhateverOrderTheyAreRegisteredInAsync()
    {
        var diagram = new CanvasFormat("text/vnd.mermaid", "Mermaid", "Write diagrams as Mermaid.") { Order = 20, Requires = ["image/svg+xml"] };
        await using var composition = Compose(diagram, Sketch.Markdown with { Order = 10 }, Sketch.Svg with { Order = 0 });
        using var schema = JsonDocument.Parse(Assert.Single(composition.All<HarnessTool>()).InputSchema);

        Assert.Equal(["image/svg+xml", "text/markdown", "text/vnd.mermaid"], OfferedIn(schema));
    }

    [Fact]
    public async Task AFormatIsOfferedOnlyWhenEveryFormatItRequiresIsOfferedAsync()
    {
        var diagram = new CanvasFormat("text/vnd.mermaid", "Mermaid", "Write diagrams as Mermaid.") { Requires = ["image/svg+xml"] };
        var chart = new CanvasFormat("text/x-chart", "Chart", "Write charts.") { Requires = ["text/vnd.mermaid"] };
        await using var without = Compose(diagram, chart, Sketch.Markdown);
        await using var with = Compose(diagram, chart, Sketch.Markdown, Sketch.Svg);

        using var missing = JsonDocument.Parse(Assert.Single(without.All<HarnessTool>()).InputSchema);
        using var present = JsonDocument.Parse(Assert.Single(with.All<HarnessTool>()).InputSchema);

        Assert.Equal(["text/markdown"], OfferedIn(missing));
        Assert.Equal(["text/vnd.mermaid", "text/x-chart", "text/markdown", "image/svg+xml"], OfferedIn(present));
    }

    [Fact]
    public async Task TheDescriptionTellsTheAgentEachOfferedFormatAndWhatToDrawInItAsync()
    {
        await using var composition = Compose(Sketch.Svg, Sketch.Markdown);
        var description = Assert.Single(composition.All<HarnessTool>()).Description;

        Assert.Contains("- image/svg+xml (SVG): Draw every diagram as SVG.", description, StringComparison.Ordinal);
        Assert.Contains("- text/markdown (Markdown): Write notes as Markdown.", description, StringComparison.Ordinal);
        Assert.Contains("A canvas in any other media type is not drawn", description, StringComparison.Ordinal);
        Assert.DoesNotContain("mermaid", description, StringComparison.OrdinalIgnoreCase);
    }

    private static string?[] OfferedIn(JsonDocument schema) =>
        [.. schema.RootElement.GetProperty("properties").GetProperty("mediaType").GetProperty("enum").EnumerateArray().Select(type => type.GetString())];

    private static PluginComposition Compose(params CanvasFormat[] formats) =>
        PluginComposition.Of(new CanvasPlugin(), new AvalaPaths("unused"), services =>
        {
            foreach (var format in formats)
            {
                services.AddSingleton(format);
            }
        });
}
