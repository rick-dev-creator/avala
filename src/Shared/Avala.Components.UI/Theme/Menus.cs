using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avala.Components.UI.Theme;

public sealed class Menus
{
    public static readonly AttachedProperty<bool> OpensUnhighlightedProperty =
        AvaloniaProperty.RegisterAttached<Menus, MenuBase, bool>("OpensUnhighlighted");

    public static readonly AttachedProperty<bool> IsHighlightingProperty =
        AvaloniaProperty.RegisterAttached<Menus, Control, bool>("IsHighlighting", defaultValue: true, inherits: true);

    static Menus() =>
        OpensUnhighlightedProperty.Changed.AddClassHandler<MenuBase>((menu, change) =>
        {
            menu.AttachedToVisualTree -= OnAttached;
            menu.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            menu.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);

            if (change.NewValue is true)
            {
                menu.AttachedToVisualTree += OnAttached;
                menu.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
                menu.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            }
        });

    private Menus()
    {
    }

    public static bool GetOpensUnhighlighted(MenuBase menu) => menu.GetValue(OpensUnhighlightedProperty);

    public static void SetOpensUnhighlighted(MenuBase menu, bool value) => menu.SetValue(OpensUnhighlightedProperty, value);

    public static bool GetIsHighlighting(Control control) => control.GetValue(IsHighlightingProperty);

    public static void SetIsHighlighting(Control control, bool value) => control.SetValue(IsHighlightingProperty, value);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is MenuBase menu)
        {
            SetIsHighlighting(menu, false);
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is MenuBase menu && !GetIsHighlighting(menu))
        {
            SetIsHighlighting(menu, true);
            e.Handled = e.Key is Key.Down or Key.Up;
        }
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is MenuBase menu && !GetIsHighlighting(menu))
        {
            SetIsHighlighting(menu, true);
        }
    }
}
