using System.Globalization;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public sealed class EnumIs : IValueConverter
{
    public static EnumIs Any { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum member
        && parameter is string names
        && names.Split(',', StringSplitOptions.TrimEntries).Contains(member.ToString(), StringComparer.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
