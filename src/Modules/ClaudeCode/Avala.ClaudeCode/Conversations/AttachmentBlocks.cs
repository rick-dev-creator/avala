using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Conversations;

internal static class AttachmentBlocks
{
    public static string[] Accepted { get; } = ["image/png", "image/jpeg", "image/gif", "image/webp", "application/pdf", "text/*"];

    public static async Task<IReadOnlyList<JsonNode>> ReadAsync(IReadOnlyList<Attachment> attachments, CancellationToken cancellationToken)
    {
        var blocks = new List<JsonNode>();

        foreach (var attachment in attachments)
        {
            blocks.Add(attachment.IsText
                ? Text(attachment, await File.ReadAllTextAsync(attachment.Path, cancellationToken))
                : Binary(attachment, Convert.ToBase64String(await File.ReadAllBytesAsync(attachment.Path, cancellationToken))));
        }

        return blocks;
    }

    private static JsonObject Text(Attachment attachment, string content) => new()
    {
        ["type"] = "text",
        ["text"] = $"<attachment name=\"{attachment.Name}\">\n{content}\n</attachment>",
    };

    private static JsonObject Binary(Attachment attachment, string data) => attachment.IsImage
        ? new JsonObject
        {
            ["type"] = "image",
            ["source"] = Source(attachment, data),
        }
        : new JsonObject
        {
            ["type"] = "document",
            ["source"] = Source(attachment, data),
            ["title"] = attachment.Name,
        };

    private static JsonObject Source(Attachment attachment, string data) => new()
    {
        ["type"] = "base64",
        ["media_type"] = attachment.MediaType,
        ["data"] = data,
    };
}
