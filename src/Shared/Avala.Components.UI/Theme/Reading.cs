using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Theme;

public sealed class Reading
{
    public const string WidthKey = "ReadingWidth";

    public const double Narrowest = 720;

    public static readonly AttachedProperty<bool> IsWideProperty =
        AvaloniaProperty.RegisterAttached<Reading, Visual, bool>("IsWide", inherits: true);

    public static readonly AttachedProperty<bool> FollowsProperty =
        AvaloniaProperty.RegisterAttached<Reading, Control, bool>("Follows");

    private const double Step = 40;

    static Reading() => FollowsProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    private Reading()
    {
    }

    public static bool GetIsWide(Visual visual) => visual.GetValue(IsWideProperty);

    public static void SetIsWide(Visual visual, bool value) => visual.SetValue(IsWideProperty, value);

    public static bool GetFollows(Control control) => control.GetValue(FollowsProperty);

    public static void SetFollows(Control control, bool value) => control.SetValue(FollowsProperty, value);

    public static double Width(double content, bool wide)
    {
        var (share, widest) = wide ? (0.8, 1200d) : (0.6, 880d);

        return Math.Clamp(Math.Floor(content * share / Step) * Step, Narrowest, widest);
    }

    private static void Watch(Control control)
    {
        control.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.BoundsProperty || change.Property == IsWideProperty || change.Property == Chrome.IsFocusModeProperty)
            {
                Fit(control);
            }
        };
        Fit(control);
    }

    private static void Fit(Control control)
    {
        var width = Width(control.Bounds.Width, GetIsWide(control) || Chrome.GetIsFocusMode(control));

        if (!control.Resources.TryGetValue(WidthKey, out var current) || current is not double known || Math.Abs(known - width) >= 1)
        {
            control.Resources[WidthKey] = width;
        }
    }
}
