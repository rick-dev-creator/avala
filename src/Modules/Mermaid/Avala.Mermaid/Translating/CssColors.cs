using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Sdk;

namespace Avala.Mermaid.Translating;

internal readonly record struct CssColor(double Red, double Green, double Blue, double Alpha)
{
    public string Hex =>
        Alpha >= 0.999
            ? string.Create(CultureInfo.InvariantCulture, $"#{Byte(Red):X2}{Byte(Green):X2}{Byte(Blue):X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{Byte(Red):X2}{Byte(Green):X2}{Byte(Blue):X2}{Byte(Alpha):X2}");

    private static int Byte(double channel) => (int)Math.Round(Math.Clamp(channel, 0, 1) * 255, MidpointRounding.AwayFromZero);
}

internal static partial class CssColors
{
    public static Option<CssColor> Parse(string text) => Read(text) is { } color ? color : Option<CssColor>.None;

    public static string Mix(string expression)
    {
        var mixed = expression;
        string before;

        do
        {
            before = mixed;
            mixed = MixPattern().Replace(before, match => Mixed(match) ?? match.Value);
        }
        while (mixed != before);

        return mixed;
    }

    private static CssColor? Read(string text)
    {
        var value = text.Trim().ToLowerInvariant();

        return value switch
        {
            "black" => new CssColor(0, 0, 0, 1),
            "white" => new CssColor(1, 1, 1, 1),
            "transparent" => new CssColor(0, 0, 0, 0),
            _ => Hex(value),
        };
    }

    private static string? Mixed(Match match)
    {
        if (Read(match.Groups["first"].Value) is not { } first || Read(match.Groups["second"].Value) is not { } second)
        {
            return null;
        }

        var (left, right) = Weights(match.Groups["p"], match.Groups["q"]);
        var alpha = (first.Alpha * left) + (second.Alpha * right);

        if (alpha <= 0)
        {
            return new CssColor(0, 0, 0, 0).Hex;
        }

        double Channel(double a, double b) => ((a * first.Alpha * left) + (b * second.Alpha * right)) / alpha;

        return new CssColor(Channel(first.Red, second.Red), Channel(first.Green, second.Green), Channel(first.Blue, second.Blue), alpha).Hex;
    }

    private static (double Left, double Right) Weights(Group first, Group second)
    {
        var left = first.Success ? Percent(first.Value) : (double?)null;
        var right = second.Success ? Percent(second.Value) : (double?)null;

        return (left, right) switch
        {
            ({ } p, { } q) when p + q > 0 => (p / (p + q), q / (p + q)),
            ({ } p, null) => (p, 1 - p),
            (null, { } q) => (1 - q, q),
            _ => (0.5, 0.5),
        };
    }

    private static double Percent(string text) =>
        Math.Clamp(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) / 100, 0, 1);

    private static CssColor? Hex(string value)
    {
        if (!HexPattern().IsMatch(value))
        {
            return null;
        }

        var digits = value[1..];
        digits = digits.Length is 3 or 4 ? string.Concat(digits.Select(digit => $"{digit}{digit}")) : digits;
        double Part(int index) => int.Parse(digits.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

        return new CssColor(Part(0), Part(1), Part(2), digits.Length == 8 ? Part(3) : 1);
    }

    [GeneratedRegex("^#([0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"color-mix\(\s*in\s+srgb\s*,\s*(?<first>#[0-9A-Fa-f]{3,8}|[a-z]+)\s*(?:(?<p>[0-9.]+)%)?\s*,\s*(?<second>#[0-9A-Fa-f]{3,8}|[a-z]+)\s*(?:(?<q>[0-9.]+)%)?\s*\)")]
    private static partial Regex MixPattern();
}
