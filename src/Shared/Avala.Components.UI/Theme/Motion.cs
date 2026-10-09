using Avalonia;

namespace Avala.Components.UI.Theme;

public sealed class Motion
{
    public static readonly AttachedProperty<bool> IsReducedProperty =
        AvaloniaProperty.RegisterAttached<Motion, Visual, bool>("IsReduced", inherits: true);

    private Motion()
    {
    }

    public static bool GetIsReduced(Visual visual) => visual.GetValue(IsReducedProperty);

    public static void SetIsReduced(Visual visual, bool value) => visual.SetValue(IsReducedProperty, value);
}
