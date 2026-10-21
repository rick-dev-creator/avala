using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Avala.Components.UI.States;

public sealed class BrandColor : IValueConverter
{
    public static BrandColor Brush { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } hex && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : BindingOperations.DoNothing;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
