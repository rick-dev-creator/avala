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

    public static readonly AttachedProperty<TimeProvider> ClockProperty =
        AvaloniaProperty.RegisterAttached<Interruptions, Control, TimeProvider>("Clock", TimeProvider.System, inherits: true);

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(0.5);

    private static readonly AttachedProperty<long> ReachedProperty =
        AvaloniaProperty.RegisterAttached<Interruptions, Control, long>("Reached");

    private static readonly Key[] Back = [Key.PageUp, Key.Home, Key.Up];

    static Interruptions() => CommandProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    private Interruptions()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    public static TimeProvider GetClock(Control control) => control.GetValue(ClockProperty);

    public static void SetClock(Control control, TimeProvider value) => control.SetValue(ClockProperty, value);

    private static void Watch(Control control)
    {
        control.RemoveHandler(InputElement.TextInputEvent, Typed);
        control.RemoveHandler(InputElement.PointerWheelChangedEvent, Reached);
        control.RemoveHandler(InputElement.PointerPressedEvent, Reached);
        control.RemoveHandler(InputElement.PointerMovedEvent, Dragged);
        control.RemoveHandler(InputElement.KeyDownEvent, Pressed);
        control.RemoveHandler(InputElement.ScrollGestureEvent, Reached);
        control.RemoveHandler(ScrollViewer.ScrollChangedEvent, Scrolled);
        control.AddHandler(InputElement.TextInputEvent, Typed, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.PointerWheelChangedEvent, Reached, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.PointerPressedEvent, Reached, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.PointerMovedEvent, Dragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.KeyDownEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        control.AddHandler(InputElement.ScrollGestureEvent, Reached, RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(ScrollViewer.ScrollChangedEvent, Scrolled, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void Typed(object? sender, TextInputEventArgs e) => Interrupt(sender);

    private static void Reached(object? sender, RoutedEventArgs e) => Arm(sender);

    private static void Dragged(object? sender, PointerEventArgs e)
    {
        if (sender is Visual visual && e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed)
        {
            Arm(sender);
        }
    }

    private static void Pressed(object? sender, KeyEventArgs e)
    {
        if (Back.Contains(e.Key))
        {
            Arm(sender);
        }
    }

    private static void Scrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y < 0
            && e.Source is ScrollViewer { TemplatedParent: not TextBox }
            && sender is Control control
            && control.GetValue(ReachedProperty) is var reached and > 0
            && GetClock(control).GetElapsedTime(reached) <= Window)
        {
            control.ClearValue(ReachedProperty);
            Interrupt(sender);
        }
    }

    private static void Arm(object? sender)
    {
        if (sender is Control control)
        {
            control.SetValue(ReachedProperty, GetClock(control).GetTimestamp());
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
