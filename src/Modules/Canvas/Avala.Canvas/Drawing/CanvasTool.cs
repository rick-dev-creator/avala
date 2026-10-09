using Avala.Agents.Contracts.Sessions;

namespace Avala.Canvas.Drawing;

internal static class CanvasTool
{
    private const string Description =
        "Draw a canvas the user sees beside the conversation: a chart, a diagram, a screen or a design. "
        + "Give it a title, a media type and its full content. Each call draws a new canvas.";

    private const string InputSchema = """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "A short title shown above the canvas." },
            "mediaType": {
              "type": "string",
              "enum": ["text/html", "image/svg+xml", "text/vnd.mermaid", "text/markdown"],
              "description": "How the content is rendered."
            },
            "content": { "type": "string", "description": "The full content of the canvas." }
          },
          "required": ["title", "mediaType", "content"],
          "additionalProperties": false
        }
        """;

    public static HarnessTool Definition { get; } = new("canvas", Description, InputSchema, ToolSurface.Canvas);
}
