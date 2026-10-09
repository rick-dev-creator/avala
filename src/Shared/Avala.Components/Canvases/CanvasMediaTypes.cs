namespace Avala.Components.Canvases;

public static class CanvasMediaTypes
{
    public const string Markdown = "text/markdown";
    public const string Svg = "image/svg+xml";
    public const string Mermaid = "text/vnd.mermaid";
    public const string Html = "text/html";

    public static string Essence(string mediaType) =>
        mediaType.Split(';', 2)[0].Trim().ToLowerInvariant();

    public static string Label(string mediaType) => Essence(mediaType) switch
    {
        Markdown => "Markdown",
        Svg => "SVG",
        Mermaid => "Mermaid",
        Html => "HTML",
        var other => other,
    };
}
