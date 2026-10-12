using System.Windows.Input;
using Avala.Components.UI.Markdown;
using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.States;

public sealed class Fitted
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Fitted, Control, ICommand>("Command", OpensNothing.Instance);

    private static readonly AttachedProperty<bool> WatchedProperty =
        AvaloniaProperty.RegisterAttached<Fitted, Control, bool>("Watched");

    static Fitted() => CommandProperty.Changed.AddClassHandler<Control>((control, _) =>
    {
        if (!control.GetValue(WatchedProperty))
        {
            control.SetValue(WatchedProperty, true);
            control.SizeChanged += (_, _) => Fit(control);
        }

        Fit(control);
    });

    private Fitted()
    {
    }

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    private static void Fit(Control control)
    {
        var scaling = TopLevel.GetTopLevel(control) is { } top ? top.RenderScaling : 1;
        var pixels = (int)Math.Ceiling(control.Bounds.Width * scaling);
        var command = GetCommand(control);

        if (pixels > 0 && command.CanExecute(pixels))
        {
            command.Execute(pixels);
        }
    }
}
