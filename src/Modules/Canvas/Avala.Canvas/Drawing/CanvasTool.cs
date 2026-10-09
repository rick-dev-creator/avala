using System.Text.Json;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Canvas.Drawing;

internal static class CanvasTool
{
    public const string Name = "canvas";

    public static HarnessTool For(CanvasOffer offer) =>
        new(Name, Description(offer), InputSchema(offer), ToolSurface.Canvas);

    private static string Description(CanvasOffer offer) =>
        string.Join(
            "\n",
            offer.Formats
                .Select(format => $"- {format.MediaType} ({format.Name}): {format.Guidance}")
                .Prepend("Offered media types:")
                .Prepend(
                    "Draw a canvas the user sees beside the conversation: a diagram, a chart, a screen, a design or a document. "
                    + "Give it a title, one of the offered media types and its full content. Each call draws a new canvas.")
                .Append("A canvas in any other media type is not drawn: the user only sees its source."));

    private static string InputSchema(CanvasOffer offer) => $$"""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "A short title shown above the canvas." },
            "mediaType": {
              "type": "string",
              "enum": {{JsonSerializer.Serialize(offer.Formats.Select(format => format.MediaType))}},
              "description": "How the content is drawn: one of the media types Avala offers."
            },
            "content": { "type": "string", "description": "The full content of the canvas." }
          },
          "required": ["title", "mediaType", "content"],
          "additionalProperties": false
        }
        """;
}
