using System.Text.Json;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal static class CanvasPrompt
{
    private static readonly Dictionary<string, string> Visual = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/svg+xml"] = "SVG",
        ["text/vnd.mermaid"] = "Mermaid",
    };

    public static Option<string> For(IReadOnlyList<HarnessTool> tools, Option<PreviewOffer> preview)
    {
        var parts = new[]
        {
            tools.FirstOrDefault(tool => tool.Surface == ToolSurface.Canvas) is { } canvas ? Canvas(canvas, preview.IsSome) : string.Empty,
            preview.Match(Pages, () => string.Empty),
        }.Where(part => part.Length > 0).ToList();

        return parts.Count > 0 ? string.Join(" ", parts) : Option<string>.None;
    }

    private static string Canvas(HarnessTool canvas, bool previewed) =>
        "Avala shows the person your work inside its conversation. When you are asked to show something visual, such as a chart, a diagram or a drawing, "
        + $"call the {CommandLine.Qualified(canvas.Name)} tool with {Formats(canvas.InputSchema)}: Avala draws it right there, as a still picture. "
        + "Do not write an HTML file or open a browser to show it, unless it must move or respond to input, such as an animation or a three.js scene: "
        + (previewed
            ? "then make it a page, as follows."
            : "then write a self-contained HTML file and give its path, which the person opens in their browser from Avala.");

    private static string Pages(PreviewOffer offer) =>
        $"For a page or an app, keep every file in the worktree, scratch files included, and vendor its libraries into it instead of loading them from a CDN. "
        + $"Serve it bound to 127.0.0.1 on port ${offer.PortVariable}, or start its dev server there; the ports ${offer.PortsVariable} are yours. "
        + $"Then tell the person its URL, such as http://localhost:${offer.PortVariable}: Avala shows it beside the conversation. "
        + "Never open a browser or take screenshots.";

    private static string Formats(string schema)
    {
        var offered = MediaTypes(schema);
        var names = offered.Where(Visual.ContainsKey).Select(type => Visual[type]).ToList();

        return names.Count > 0 ? string.Join(" or ", names) : "one of the media types it offers";
    }

    private static List<string> MediaTypes(string schema)
    {
        try
        {
            using var document = JsonDocument.Parse(schema);

            return document.RootElement.TryGetProperty("properties", out var properties)
                && properties.TryGetProperty("mediaType", out var mediaType)
                && mediaType.TryGetProperty("enum", out var values)
                && values.ValueKind == JsonValueKind.Array
                    ? [.. values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!)]
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
