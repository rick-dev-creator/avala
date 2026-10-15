using System.Windows.Input;
using Avala.Components.UI.Markdown;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Avala.Components.UI.States;

public sealed class Dropping
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Dropping, Control, ICommand>("Command", OpensNothing.Instance);

    static Dropping() => CommandProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        if (change.GetOldValue<ICommand>() == OpensNothing.Instance)
        {
            DragDrop.SetAllowDrop(control, true);
            control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            control.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    });

    private Dropping()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var files = (e.DataTransfer.TryGetFiles() ?? []).Select(file => file.TryGetLocalPath()).OfType<string>().ToList();

        if (files.Count > 0)
        {
            GetCommand(control).Execute(files);
            e.Handled = true;
        }
    }
}
