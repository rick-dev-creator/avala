namespace Avala.Components.Canvases;

public sealed record CanvasRendering(string MediaType, string Content, bool IsFinal, int Version)
{
    public static CanvasRendering Nothing { get; } = new(string.Empty, string.Empty, true, 0);

    public bool IsNothing => Version == 0;

    public Guid Surface { get; init; }

    public bool IsOffered { get; init; } = true;

    public bool Follows(CanvasRendering earlier) => Surface == earlier.Surface && Version > earlier.Version;

    public const int RenderableLength = 512 * 1024;

    public const int SourcePreviewLength = 64 * 1024;

    public string Essence => CanvasMediaTypes.Essence(MediaType);

    public bool IsRenderable => Content.Length <= RenderableLength;

    public string SourcePreview => Content.Length <= SourcePreviewLength ? Content : Content[..SourcePreviewLength];

    public int HiddenSourceLength => Math.Max(0, Content.Length - SourcePreviewLength);
}
