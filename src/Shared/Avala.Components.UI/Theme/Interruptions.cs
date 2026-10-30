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

    private static readonly Key[] Back = [Key.PageUp, Key.Home, Key.Up];

    static Interruptions() => CommandProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    private Interruptions()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    private static void Watch(Control control)
    {
        control.RemoveHandler(InputElement.TextInputEvent, Typed);
        control.RemoveHandler(InputElement.PointerWheelChangedEvent, Wheeled);
        control.RemoveHandler(InputElement.KeyDownEvent, Pressed);
        control.AddHandler(InputElement.TextInputEvent, Typed, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.PointerWheelChangedEvent, Wheeled, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.KeyDownEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void Typed(object? sender, TextInputEventArgs e) => Interrupt(sender);

    private static void Wheeled(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0)
        {
            Interrupt(sender);
        }
    }

    private static void Pressed(object? sender, KeyEventArgs e)
    {
        if (Back.Contains(e.Key))
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
