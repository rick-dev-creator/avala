using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avala.Components.UI.States;

public sealed class Resizer
{
    public const string Grip = "grip";

    public const string Resizing = "resizing";

    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Resizer, Control, ICommand>("Command");

    public static readonly AttachedProperty<ICommand> CommittedProperty =
        AvaloniaProperty.RegisterAttached<Resizer, Control, ICommand>("Committed");

    public static readonly AttachedProperty<ICommand> ResetProperty =
        AvaloniaProperty.RegisterAttached<Resizer, Control, ICommand>("Reset");

    public static readonly AttachedProperty<bool> FromLeftProperty =
        AvaloniaProperty.RegisterAttached<Resizer, Control, bool>("FromLeft");

    private static readonly AttachedProperty<Drag?> DragProperty =
        AvaloniaProperty.RegisterAttached<Resizer, Control, Drag?>("Drag");

    static Resizer() => CommandProperty.Changed.AddClassHandler<Control>((pane, change) =>
    {
        if (change.OldValue is null)
        {
            Watch(pane);
        }
    });

    private Resizer()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    public static ICommand GetCommitted(Control control) => control.GetValue(CommittedProperty);

    public static void SetCommitted(Control control, ICommand value) => control.SetValue(CommittedProperty, value);

    public static ICommand GetReset(Control control) => control.GetValue(ResetProperty);

    public static void SetReset(Control control, ICommand value) => control.SetValue(ResetProperty, value);

    public static bool GetFromLeft(Control control) => control.GetValue(FromLeftProperty);

    public static void SetFromLeft(Control control, bool value) => control.SetValue(FromLeftProperty, value);

    private static void Watch(Control pane)
    {
        pane.AddHandler(InputElement.PointerPressedEvent, (_, e) => Press(pane, e), RoutingStrategies.Tunnel);
        pane.AddHandler(InputElement.PointerMovedEvent, (_, e) => Move(pane, e), RoutingStrategies.Tunnel);
        pane.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Release(pane, e), RoutingStrategies.Tunnel);
        pane.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(pane), RoutingStrategies.Bubble);
    }

    private static void Press(Control pane, PointerPressedEventArgs e)
    {
        if (OnGrip(e) is not { } grip || !e.GetCurrentPoint(pane).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;

        if (e.ClickCount > 1)
        {
            Run(GetReset(pane));

            return;
        }

        e.Pointer.Capture(grip);
        pane.SetValue(DragProperty, new Drag(e.GetPosition(null).X, pane.Bounds.Width));
    }

    private static void Move(Control pane, PointerEventArgs e)
    {
        if (pane.GetValue(DragProperty) is not { } drag)
        {
            return;
        }

        var moved = e.GetPosition(null).X - drag.From;
        var width = GetFromLeft(pane) ? drag.Width - moved : drag.Width + moved;

        if (Math.Abs(width - pane.Bounds.Width) >= 1 && GetCommand(pane) is { } command && command.CanExecute(width))
        {
            pane.Classes.Add(Resizing);
            command.Execute(width);
        }

        e.Handled = true;
    }

    private static void Release(Control pane, PointerReleasedEventArgs e)
    {
        if (pane.GetValue(DragProperty) is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        End(pane);
        e.Handled = true;
    }

    private static void End(Control pane)
    {
        if (pane.GetValue(DragProperty) is null)
        {
            return;
        }

        pane.ClearValue(DragProperty);

        if (pane.Classes.Remove(Resizing))
        {
            Run(GetCommitted(pane));
        }
    }

    private static Control? OnGrip(RoutedEventArgs e)
    {
        for (var current = e.Source as StyledElement; current is not null; current = current.Parent)
        {
            if (current is Control control && control.Classes.Contains(Grip))
            {
                return control;
            }
        }

        return null;
    }

    private static void Run(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }

    private sealed record Drag(double From, double Width);
}
