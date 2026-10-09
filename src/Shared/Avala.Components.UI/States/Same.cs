using System.Globalization;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public sealed class Same : IMultiValueConverter
{
    public static Same Item { get; } = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 2 && values[0] is { } first && ReferenceEquals(first, values[1]);
}
