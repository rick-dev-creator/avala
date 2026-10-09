namespace Avala.Canvas.Contracts;

public sealed record CanvasFormat(string MediaType, string Name, string Guidance)
{
    public int Order { get; init; } = 100;

    public IReadOnlyList<string> Requires { get; init; } = [];

    public bool Matches(string mediaType) =>
        string.Equals(MediaType, mediaType.Split(';', 2)[0].Trim(), StringComparison.OrdinalIgnoreCase);
}
