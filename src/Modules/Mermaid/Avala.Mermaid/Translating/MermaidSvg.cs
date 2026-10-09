using Avala.Sdk;
using Mermaider;
using Mermaider.Models;

namespace Avala.Mermaid.Translating;

internal sealed record MermaidPalette(string Background, string Foreground, string Accent, string Muted)
{
    public static MermaidPalette Dark { get; } = new("#17181C", "#EDEDEF", "#8DA2FB", "#A3A3AD");
}

internal static class MermaidSvg
{
    public static Option<string> Draw(string source, MermaidPalette palette)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Option<string>.None;
        }

        try
        {
            return CssVariables.Flatten(MermaidRenderer.RenderSvg(source, Options(palette)));
        }
        catch (Exception exception) when (exception is MermaidParseException or MermaidRenderException or MermaidSvgException or ArgumentException or InvalidOperationException)
        {
            return Option<string>.None;
        }
    }

    private static RenderOptions Options(MermaidPalette palette) => new()
    {
        Bg = palette.Background,
        Fg = palette.Foreground,
        Accent = palette.Accent,
        Muted = palette.Muted,
        Font = "Inter",
        MonoFont = "JetBrains Mono",
        Transparent = true,
        Elevation = 0,
        Padding = 16,
    };
}
