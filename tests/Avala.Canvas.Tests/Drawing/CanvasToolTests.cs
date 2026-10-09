using System.Text.Json;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Drawing;

namespace Avala.Canvas.Tests.Drawing;

public sealed class CanvasToolTests
{
    [Fact]
    public void TheCanvasToolSurfacesAsCanvasesAndAsksForATitleAMediaTypeAndTheContent()
    {
        var tool = CanvasTool.Definition;
        using var schema = JsonDocument.Parse(tool.InputSchema);

        Assert.Equal((ToolSurface.Canvas, "canvas"), (tool.Surface, tool.Name));
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            ["title", "mediaType", "content"],
            schema.RootElement.GetProperty("required").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(
            ["text/html", "image/svg+xml", "text/vnd.mermaid", "text/markdown"],
            schema.RootElement.GetProperty("properties").GetProperty("mediaType").GetProperty("enum").EnumerateArray().Select(type => type.GetString()));
    }
}
