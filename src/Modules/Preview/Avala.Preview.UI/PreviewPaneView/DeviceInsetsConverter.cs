using Avala.Preview.Pane;
using Avalonia;
using Avalonia.Data.Converters;

namespace Avala.Preview.UI;

internal static class DeviceInsetsConverter
{
    public static IValueConverter Margin { get; } = new FuncValueConverter<DeviceInsets, Thickness>(insets =>
        insets is null ? default : new Thickness(insets.Left, insets.Top, insets.Right, insets.Bottom));
}
