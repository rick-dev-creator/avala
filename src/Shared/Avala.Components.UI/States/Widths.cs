using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.States;

public sealed class Widths
{
    public static readonly AttachedProperty<ICommand> ReportProperty =
        AvaloniaProperty.RegisterAttached<Widths, Control, ICommand>("Report");

    static Widths() => ReportProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    private Widths()
    {
    }

    public static ICommand GetReport(Control control) => control.GetValue(ReportProperty);

    public static void SetReport(Control control, ICommand value) => control.SetValue(ReportProperty, value);

    private static void Watch(Control control)
    {
        control.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.BoundsProperty && Math.Abs(change.GetOldValue<Rect>().Width - change.GetNewValue<Rect>().Width) >= 1)
            {
                Report(control);
            }
        };
        Report(control);
    }

    private static void Report(Control control)
    {
        var width = control.Bounds.Width;

        if (width > 0 && GetReport(control) is { } command && command.CanExecute(width))
        {
            command.Execute(width);
        }
    }
}
