using System.Globalization;
using Avala.Components.Status;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public sealed class StatusIs : IValueConverter
{
    public static StatusIs Any { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StatusKind kind
        && parameter is string kinds
        && kinds.Split(',', StringSplitOptions.TrimEntries).Contains(kind.ToString(), StringComparer.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
