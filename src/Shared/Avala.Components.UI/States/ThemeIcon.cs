using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Avala.Components.UI.States;

public sealed class ThemeIcon : IValueConverter
{
    public static ThemeIcon Named { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        $"{parameter}{value}" is { Length: > 0 } key
        && Application.Current is { } application
        && application.TryGetResource(key, application.ActualThemeVariant, out var found)
        && found is Geometry geometry
            ? geometry
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
