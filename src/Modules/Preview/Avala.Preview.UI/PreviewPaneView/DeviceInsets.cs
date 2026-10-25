using Avalonia;
using Avalonia.Data.Converters;

namespace Avala.Preview.UI;

internal static class DeviceInsets
{
    public static IValueConverter Around { get; } = new FuncValueConverter<double, Thickness>(bezel => new Thickness(bezel));
}
