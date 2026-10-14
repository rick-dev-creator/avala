using Avalonia.Input;

namespace Avala.Components.UI.Copying;

public static class Hotkeys
{
    public static KeyModifiers Command { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture Copy { get; } = new(Key.C, Command);

    public static KeyGesture SelectAll { get; } = new(Key.A, Command);

    public static KeyGesture CommandEnter { get; } = new(Key.Enter, Command);

    public static KeyGesture CommandShiftEnter { get; } = new(Key.Enter, Command | KeyModifiers.Shift);

    public static KeyGesture CommandBack { get; } = new(Key.Back, Command);

    public static KeyGesture Enter { get; } = new(Key.Enter);

    public static KeyGesture Review { get; } = new(Key.R, Command);

    public static KeyGesture Decisions { get; } = new(Key.D, Command);

    public static KeyGesture NewJob { get; } = new(Key.N, Command);

    public static KeyGesture Inspector { get; } = new(Key.I, Command);

    public static KeyGesture FocusMode { get; } = new(Key.F, Command | KeyModifiers.Shift);

    public static KeyGesture Archive { get; } = new(Key.A, Command | KeyModifiers.Shift);

    public static KeyGesture Sidebar { get; } = new(Key.OemPipe, Command);

    public static KeyGesture SidebarBackslash { get; } = new(Key.OemBackslash, Command);

    public static string CommandEnterLabel { get; } = Label(CommandEnter);

    public static string CommandShiftEnterLabel { get; } = Label(CommandShiftEnter);

    public static string CommandBackLabel { get; } = Label(CommandBack);

    public static string ReviewLabel { get; } = Label(Review);

    public static string DecisionsLabel { get; } = Label(Decisions);

    public static string NewJobLabel { get; } = Label(NewJob);

    public static string InspectorLabel { get; } = Label(Inspector);

    public static string FocusModeLabel { get; } = Label(FocusMode);

    public static string ArchiveLabel { get; } = Label(Archive);

    public static string SidebarLabel { get; } = Label(Sidebar);

    public static string Label(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        return Spelled(gesture, OperatingSystem.IsMacOS() ? Keyboard.Mac : Keyboard.Other);
    }

    private static string Spelled(KeyGesture gesture, Keyboard keyboard) =>
        string.Concat(
            gesture.KeyModifiers.HasFlag(KeyModifiers.Control) ? keyboard.Control : string.Empty,
            gesture.KeyModifiers.HasFlag(KeyModifiers.Alt) ? keyboard.Alt : string.Empty,
            gesture.KeyModifiers.HasFlag(KeyModifiers.Meta) ? keyboard.Meta : string.Empty,
            gesture.KeyModifiers.HasFlag(KeyModifiers.Shift) ? keyboard.Shift : string.Empty,
            keyboard.Name(gesture.Key));

    private sealed record Keyboard(string Control, string Alt, string Meta, string Shift, string EnterName, string BackName)
    {
        public static Keyboard Mac { get; } = new("⌃", "⌥", "⌘", "⇧", "Return", "Delete");

        public static Keyboard Other { get; } = new("Ctrl+", "Alt+", string.Empty, "Shift+", "Enter", "Backspace");

        public string Name(Key key) => key switch
        {
            Key.Enter => EnterName,
            Key.Back => BackName,
            Key.OemPipe or Key.OemBackslash => "\\",
            _ => key.ToString(),
        };
    }
}
