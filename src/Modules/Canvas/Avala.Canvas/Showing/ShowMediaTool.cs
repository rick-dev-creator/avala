using Avala.Agents.Contracts.Sessions;

namespace Avala.Canvas.Showing;

internal static class ShowMediaTool
{
    public const string Name = "show_media";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Show the person an image in the conversation, such as a screenshot of the screen you are building, as evidence of how the work looks. "
        + "Give the path of a PNG, JPEG, GIF or WebP file, relative to the working directory or absolute. "
        + "Avala keeps its own copy, so the file need not stay in the repository. Draw diagrams and designs with the canvas instead.",
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The image file, relative to the working directory or absolute." },
            "caption": { "type": "string", "description": "One short line saying what the image shows." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);
}
