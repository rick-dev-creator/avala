using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Theme;

public sealed class Chrome
{
    public const double Leading = 56;

    public const double Clearance = 44;

    public static readonly AttachedProperty<bool> IsBareProperty =
        AvaloniaProperty.RegisterAttached<Chrome, Control, bool>("IsBare");

    public static readonly AttachedProperty<bool> IsFocusModeProperty =
        AvaloniaProperty.RegisterAttached<Chrome, Visual, bool>("IsFocusMode", inherits: true);

    static Chrome()
    {
        IsBareProperty.Changed.AddClassHandler<Control>((control, _) => Fit(control));
        IsFocusModeProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.IsSet(IsBareProperty))
            {
                Fit(control);
            }
        });
    }

    private Chrome()
    {
    }

    public static bool GetIsBare(Control control) => control.GetValue(IsBareProperty);

    public static void SetIsBare(Control control, bool value) => control.SetValue(IsBareProperty, value);

    public static bool GetIsFocusMode(Visual visual) => visual.GetValue(IsFocusModeProperty);

    public static void SetIsFocusMode(Visual visual, bool value) => visual.SetValue(IsFocusModeProperty, value);

    private static void Fit(Control control)
    {
        var bare = GetIsBare(control) && !GetIsFocusMode(control);
        var leading = bare ? Leading : 0;
        control.Resources["ChromeLeading"] = new Thickness(leading, 0, 0, 0);
        control.Resources["ChromeClearance"] = new Thickness(0, bare ? Clearance : 0, 0, 0);
        control.Resources["PageHeaderPadding"] = new Thickness(24 + leading, 12, 24, 12);
    }
}
