using System.Globalization;
using System.Xml.Linq;
using Avala.Sdk;

namespace Avala.Rendering.Sizing;

internal sealed record SvgArea(double Width, double Height)
{
    private static readonly char[] Separators = [' ', ',', '\t', '\r', '\n'];

    public static Option<SvgArea> Fit(XElement root)
    {
        if (ViewBox(root) is { } box)
        {
            root.SetAttributeValue("width", box.Width.ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("height", box.Height.ToString(CultureInfo.InvariantCulture));

            return box;
        }

        return Length(root, "width") is { } width && Length(root, "height") is { } height
            ? new SvgArea(width, height)
            : Option<SvgArea>.None;
    }

    private static SvgArea? ViewBox(XElement root)
    {
        var parts = (root.Attribute("viewBox")?.Value ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 4 && Number(parts[2]) is { } width && Number(parts[3]) is { } height
            ? new SvgArea(width, height)
            : null;
    }

    private static double? Length(XElement root, string name)
    {
        var value = root.Attribute(name)?.Value.Trim() ?? string.Empty;

        return Number(value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value);
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 && double.IsFinite(value)
            ? value
            : null;
}
