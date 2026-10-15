using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.States;

public sealed class Revealed
{
    public static readonly AttachedProperty<int> IndexProperty =
        AvaloniaProperty.RegisterAttached<Revealed, ItemsControl, int>("Index", -1);

    static Revealed() => IndexProperty.Changed.AddClassHandler<ItemsControl>((items, change) =>
    {
        if (change.GetNewValue<int>() is var index and >= 0 && index < items.ItemCount)
        {
            items.ScrollIntoView(index);
        }
    });

    private Revealed()
    {
    }

    public static int GetIndex(ItemsControl items) => items.GetValue(IndexProperty);

    public static void SetIndex(ItemsControl items, int value) => items.SetValue(IndexProperty, value);
}
