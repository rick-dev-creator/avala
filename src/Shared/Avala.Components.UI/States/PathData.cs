using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Avala.Components.UI.States;

public sealed class PathData : IValueConverter
{
    private readonly Dictionary<string, Geometry?> parsed = new(StringComparer.Ordinal);

    public static PathData Parsed { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } data ? Known(data) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private Geometry? Known(string data)
    {
        if (!parsed.TryGetValue(data, out var geometry))
        {
            geometry = Parse(data);
            parsed[data] = geometry;
        }

        return geometry;
    }

    private static Geometry? Parse(string data)
    {
        try
        {
            return Geometry.Parse(data);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
