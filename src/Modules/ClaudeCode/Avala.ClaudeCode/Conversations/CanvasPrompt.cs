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

    private const string ShowMedia = "show_media";

    public static Option<string> For(IReadOnlyList<HarnessTool> tools, Option<PreviewOffer> preview)
    {
        var parts = new[]
        {
            tools.FirstOrDefault(tool => tool.Surface == ToolSurface.Canvas) is { } canvas ? Canvas(canvas, preview.IsSome) : string.Empty,
            preview.Match(Pages, () => string.Empty),
            tools.FirstOrDefault(tool => tool.Name == ShowMedia) is { } media ? Evidence(media) : string.Empty,
        }.Where(part => part.Length > 0).ToList();

        return parts.Count > 0 ? string.Join(" ", parts) : Option<string>.None;
    }

    private static string Canvas(HarnessTool canvas, bool previewed) =>
        "Avala shows the person your work inside its conversation. When you are asked to show something visual, such as a chart, a diagram or a drawing, "
        + $"call the {CommandLine.Qualified(canvas.Name)} tool with {Formats(canvas.InputSchema)}: Avala draws it right there{Animated(canvas.InputSchema)}. "
        + "Do not write an HTML file or open a browser to show it, unless it must respond to input, such as an interactive chart, a three.js scene or an app: "
        + (previewed
            ? "then make it a page, as follows."
            : "then write a self-contained HTML file and give its path, which the person opens from Avala.");

    private static string Pages(PreviewOffer offer) =>
        $"For a page or an app, keep every file in the worktree, scratch files included, and vendor its libraries into it instead of loading them from a CDN. "
        + $"Serve it bound to 127.0.0.1 on port ${offer.PortVariable}, or start its dev server there; the ports ${offer.PortsVariable} are yours. "
        + $"Then tell the person its URL, such as http://localhost:${offer.PortVariable}: Avala shows it beside the conversation. "
        + "Never open a browser for the person.";

    private static string Evidence(HarnessTool media) =>
        "When you build or change something visual, such as a screen, a page or an app, show the person how it looks: "
        + "save a screenshot or a rendered image of it outside the repository, such as in a temporary folder, so it does not land in the branch, "
        + $"and call the {CommandLine.Qualified(media.Name)} tool with its path and a short caption. "
        + "Take it headless, without opening a browser in front of the person.";

    private static string Animated(string schema) =>
        MediaTypes(schema).Contains("image/svg+xml", StringComparer.OrdinalIgnoreCase)
            ? " and plays its SVG animations written in SMIL, such as <animate> or <animateTransform>, so a chart may grow or a flow may move"
            : string.Empty;

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
