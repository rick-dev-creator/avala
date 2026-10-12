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

    public static Option<string> For(IReadOnlyList<HarnessTool> tools) =>
        tools.FirstOrDefault(tool => tool.Surface == ToolSurface.Canvas) is { } canvas
            ? $"Avala shows the person your work inside its conversation. When you are asked to show something visual, such as a chart, a diagram or a drawing, "
              + $"call the {CommandLine.Qualified(canvas.Name)} tool with {Formats(canvas.InputSchema)}: Avala draws it right there, as a still picture. "
              + "Do not write an HTML file or open a browser to show it, unless it must move or respond to input, such as an animation or a three.js scene: "
              + "then write a self-contained HTML file and give its path, which the person opens in their browser from Avala."
            : Option<string>.None;

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
