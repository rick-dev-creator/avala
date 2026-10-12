using System.Windows.Input;
using Avala.Components.UI.Markdown;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Avala.Components.UI.States;

public sealed class Realized
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Realized, Control, ICommand>("Command", OpensNothing.Instance);

    private static readonly AttachedProperty<bool> WatchedProperty =
        AvaloniaProperty.RegisterAttached<Realized, Control, bool>("Watched");

    static Realized() => CommandProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        if (!control.GetValue(WatchedProperty))
        {
            control.SetValue(WatchedProperty, true);
            control.AttachedToVisualTree += (_, _) => Run(GetCommand(control), true);
            control.DetachedFromVisualTree += (_, _) => Run(GetCommand(control), false);
        }

        if (control.IsAttachedToVisualTree())
        {
            Run(change.GetOldValue<ICommand>(), false);
            Run(change.GetNewValue<ICommand>(), true);
        }
    });

    private Realized()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    private static void Run(ICommand command, bool shown)
    {
        if (command.CanExecute(shown))
        {
            command.Execute(shown);
        }
    }
}
