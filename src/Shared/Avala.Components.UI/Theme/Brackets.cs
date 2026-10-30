using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avala.Components.UI.Theme;

public sealed class Brackets
{
    public static readonly AttachedProperty<bool> OnHoverProperty =
        AvaloniaProperty.RegisterAttached<Brackets, Control, bool>("OnHover");

    private static readonly Geometry TopLeft = Geometry.Parse("M0,8 V0 H8");

    private static readonly Geometry BottomRight = Geometry.Parse("M8,0 V8 H0");

    static Brackets() => OnHoverProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));

    private Brackets()
    {
    }

    public static bool GetOnHover(Control control) => control.GetValue(OnHoverProperty);

    public static void SetOnHover(Control control, bool value) => control.SetValue(OnHoverProperty, value);

    public static bool AreShown(Control control) => AdornerLayer.GetAdorner(control) is Panel { Name: "Brackets" };

    private static void Watch(Control control)
    {
        control.PointerEntered -= Entered;
        control.PointerExited -= Exited;
        Hide(control);

        if (GetOnHover(control))
        {
            control.PointerEntered += Entered;
            control.PointerExited += Exited;
        }
    }

    private static void Entered(object? sender, PointerEventArgs e)
    {
        if (sender is Control control && !AreShown(control))
        {
            var corners = new Panel
            {
                Name = "Brackets",
                IsHitTestVisible = false,
                Children = { Corner(TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top), Corner(BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom) },
            };
            Motion.SetEnter(corners, MotionEntrance.Close);
            AdornerLayer.SetIsClipEnabled(corners, false);
            AdornerLayer.SetAdorner(control, corners);
        }
    }

    private static void Exited(object? sender, PointerEventArgs e)
    {
        if (sender is Control control)
        {
            Hide(control);
        }
    }

    private static void Hide(Control control)
    {
        if (AreShown(control))
        {
            AdornerLayer.SetAdorner(control, null);
        }
    }

    private static Avalonia.Controls.Shapes.Path Corner(Geometry shape, HorizontalAlignment horizontal, VerticalAlignment vertical) => new()
    {
        Classes = { "bracket" },
        Data = shape,
        Width = 8,
        Height = 8,
        HorizontalAlignment = horizontal,
        VerticalAlignment = vertical,
    };
}
