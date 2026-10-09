using Avala.Canvas.Contracts;

namespace Avala.Canvas.Drawing;

internal sealed class CanvasOffer(IEnumerable<CanvasFormat> formats)
{
    public IReadOnlyList<CanvasFormat> Formats { get; } = Available([.. formats.DistinctBy(format => format.MediaType.ToLowerInvariant())]);

    public bool Offers(string mediaType) => Formats.Any(format => format.Matches(mediaType));

    private static List<CanvasFormat> Available(List<CanvasFormat> declared)
    {
        var available = declared;
        List<CanvasFormat> before;

        do
        {
            before = available;
            available = [.. before.Where(format => format.Requires.All(required => before.Any(other => other.Matches(required))))];
        }
        while (available.Count != before.Count);

        return [.. available.Select((format, index) => (format, index)).OrderBy(entry => entry.format.Order).ThenBy(entry => entry.index).Select(entry => entry.format)];
    }
}
