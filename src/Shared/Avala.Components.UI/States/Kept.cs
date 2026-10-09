using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public sealed class Kept : IValueConverter
{
    public static Kept Selection { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value ?? BindingOperations.DoNothing;
}
