using System.Globalization;
using Avalonia.Data.Converters;

namespace Avala.Workbench.UI;

internal static class SilenceMinutes
{
    public static IValueConverter Label { get; } = new FuncValueConverter<string, string>(draft =>
        double.TryParse(draft?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0
            ? seconds % 60 == 0 ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:0} min") : string.Create(CultureInfo.InvariantCulture, $"{seconds:0.###} s")
            : "not a number of seconds");
}
