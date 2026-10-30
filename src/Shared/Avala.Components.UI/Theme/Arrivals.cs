using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Theme;

public sealed class Arrivals
{
    public static readonly AttachedProperty<bool> OnlyNewEnterProperty =
        AvaloniaProperty.RegisterAttached<Arrivals, ItemsControl, bool>("OnlyNewEnter");

    private static readonly AttachedProperty<HashSet<object>?> ArrivedProperty =
        AvaloniaProperty.RegisterAttached<Arrivals, ItemsControl, HashSet<object>?>("Arrived");

    static Arrivals() => OnlyNewEnterProperty.Changed.AddClassHandler<ItemsControl>((list, _) => Track(list));

    private Arrivals()
    {
    }

    public static bool GetOnlyNewEnter(ItemsControl list) => list.GetValue(OnlyNewEnterProperty);

    public static void SetOnlyNewEnter(ItemsControl list, bool value) => list.SetValue(OnlyNewEnterProperty, value);

    private static void Track(ItemsControl list)
    {
        if (!GetOnlyNewEnter(list) || list.GetValue(ArrivedProperty) is not null)
        {
            return;
        }

        var arrived = new HashSet<object>(ReferenceEqualityComparer.Instance);
        list.SetValue(ArrivedProperty, arrived);
        list.ItemsView.CollectionChanged += (_, change) => Note(arrived, change);
        list.ContainerPrepared += (_, prepared) =>
        {
            if (prepared.Container.DataContext is { } item && arrived.Remove(item))
            {
                Motion.Release(prepared.Container);
            }
            else
            {
                Motion.Settle(prepared.Container);
            }
        };
    }

    private static void Note(HashSet<object> arrived, NotifyCollectionChangedEventArgs change)
    {
        if (change.Action == NotifyCollectionChangedAction.Add && change.NewItems is { } added)
        {
            foreach (var item in added)
            {
                arrived.Add(item);
            }
        }
        else if (change.Action == NotifyCollectionChangedAction.Reset)
        {
            arrived.Clear();
        }
    }
}
