using Avalonia.Input;

namespace Avala.Components.UI.Copying;

public static class Hotkeys
{
    public static KeyModifiers Command { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static string CommandEnterLabel { get; } = OperatingSystem.IsMacOS() ? "⌘Return" : "Ctrl+Enter";

    public static string FocusModeLabel { get; } = OperatingSystem.IsMacOS() ? "⌘⇧F" : "Ctrl+Shift+F";

    public static KeyGesture Copy { get; } = new(Key.C, Command);

    public static KeyGesture SelectAll { get; } = new(Key.A, Command);

    public static KeyGesture CommandEnter { get; } = new(Key.Enter, Command);

    public static KeyGesture Enter { get; } = new(Key.Enter);
}
