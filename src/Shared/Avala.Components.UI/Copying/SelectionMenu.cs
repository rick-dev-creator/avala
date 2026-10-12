using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Avala.Components.UI.Copying;

public static class SelectionMenu
{
    public static readonly AttachedProperty<bool> OffersProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, bool>("Offers", typeof(SelectionMenu));

    static SelectionMenu() =>
        OffersProperty.Changed.AddClassHandler<SelectableTextBlock>((text, change) =>
        {
            if (change.NewValue is true && text.ContextFlyout is not MenuFlyout { Items.Count: 2 })
            {
                text.ContextFlyout = For(text.Copy, text.SelectAll, () => text.CanCopy);
            }
        });

    public static bool GetOffers(SelectableTextBlock text) => text.GetValue(OffersProperty);

    public static void SetOffers(SelectableTextBlock text, bool offers) => text.SetValue(OffersProperty, offers);

    public static MenuFlyout For(Action copy, Action selectAll, Func<bool> canCopy)
    {
        var copying = new MenuItem { Name = "CopySelection", Header = "Copy", InputGesture = Hotkeys.Copy };
        copying.Click += (_, _) => copy();
        var all = new MenuItem { Name = "SelectAll", Header = "Select All", InputGesture = Hotkeys.SelectAll };
        all.Click += (_, _) => selectAll();
        var menu = new MenuFlyout { Placement = PlacementMode.Pointer, Items = { copying, all } };
        menu.Opening += (_, _) => copying.IsEnabled = canCopy();

        return menu;
    }
}
