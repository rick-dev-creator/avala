using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Theme;

public sealed class Chrome
{
    public const double Leading = 84;

    public const double Clearance = 44;

    public static readonly AttachedProperty<bool> IsBareProperty =
        AvaloniaProperty.RegisterAttached<Chrome, Control, bool>("IsBare");

    static Chrome() => IsBareProperty.Changed.AddClassHandler<Control>((control, _) => Fit(control));

    private Chrome()
    {
    }

    public static bool GetIsBare(Control control) => control.GetValue(IsBareProperty);

    public static void SetIsBare(Control control, bool value) => control.SetValue(IsBareProperty, value);

    private static void Fit(Control control)
    {
        var leading = GetIsBare(control) ? Leading : 0;
        control.Resources["ChromeLeading"] = new Thickness(leading, 0, 0, 0);
        control.Resources["ChromeClearance"] = new Thickness(0, GetIsBare(control) ? Clearance : 0, 0, 0);
        control.Resources["PageHeaderPadding"] = new Thickness(24 + leading, 12, 24, 12);
    }
}
