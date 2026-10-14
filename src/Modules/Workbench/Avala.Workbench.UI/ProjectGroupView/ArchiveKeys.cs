using Avala.Components.UI.Copying;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace Avala.Workbench.UI;

internal static class ArchiveKeys
{
    public static IValueConverter WhenSelected { get; } = new FuncValueConverter<bool, KeyGesture?>(selected => selected ? Hotkeys.Archive : null);

    public static string Tip { get; } = $"Archive · {Hotkeys.ArchiveLabel}";
}
