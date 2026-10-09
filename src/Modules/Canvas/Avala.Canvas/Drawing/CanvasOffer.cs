using Avala.Canvas.Contracts;

namespace Avala.Canvas.Drawing;

internal sealed class CanvasOffer(IEnumerable<CanvasFormat> formats)
{
    public IReadOnlyList<CanvasFormat> Formats { get; } = [.. formats.DistinctBy(format => format.MediaType.ToLowerInvariant())];

    public bool Offers(string mediaType) => Formats.Any(format => format.Matches(mediaType));
}
