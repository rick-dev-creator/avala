using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avala.Components.UI.Theme;

public sealed class Interruptions
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Interruptions, Control, ICommand>("Command");

    static Interruptions() => CommandProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    private Interruptions()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    private static void Watch(Control control)
    {
        control.RemoveHandler(InputElement.TextInputEvent, Typed);
        control.RemoveHandler(ScrollViewer.ScrollChangedEvent, Scrolled);
        control.AddHandler(InputElement.TextInputEvent, Typed, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(ScrollViewer.ScrollChangedEvent, Scrolled, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void Typed(object? sender, TextInputEventArgs e) => Interrupt(sender);

    private static void Scrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y < 0)
        {
            Interrupt(sender);
        }
    }

    private static void Interrupt(object? sender)
    {
        if (sender is Control control && GetCommand(control) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
