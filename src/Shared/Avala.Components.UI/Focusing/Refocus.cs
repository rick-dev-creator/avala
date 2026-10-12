using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Focusing;

public static class Refocus
{
    public static readonly AttachedProperty<int> SignalProperty =
        AvaloniaProperty.RegisterAttached<TextBox, int>("Signal", typeof(Refocus));

    static Refocus() =>
        SignalProperty.Changed.AddClassHandler<TextBox>((box, change) =>
        {
            if (change.NewValue is int signal && signal > 0)
            {
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            }
        });

    public static int GetSignal(TextBox box) => box.GetValue(SignalProperty);

    public static void SetSignal(TextBox box, int signal) => box.SetValue(SignalProperty, signal);
}
