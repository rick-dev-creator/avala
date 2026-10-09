using System.Collections.ObjectModel;

namespace Avala.Workbench.Presenting;

internal static class Reconciling
{
    public static void Reconcile<TItem, TSource, TKey>(
        this ObservableCollection<TItem> items,
        IReadOnlyList<TSource> next,
        Func<TItem, TKey> keyOf,
        Func<TSource, TKey> sourceKey,
        Func<TSource, TItem> create,
        Action<TItem, TSource> update)
        where TKey : notnull
    {
        var wanted = next.Select(sourceKey).ToHashSet();

        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(keyOf(items[index])))
            {
                items.RemoveAt(index);
            }
        }

        for (var index = 0; index < next.Count; index++)
        {
            var key = sourceKey(next[index]);
            var found = FindFrom(items, index, key, keyOf);

            if (found < 0)
            {
                items.Insert(index, create(next[index]));
                continue;
            }

            if (found != index)
            {
                items.Move(found, index);
            }

            update(items[index], next[index]);
        }
    }

    private static int FindFrom<TItem, TKey>(ObservableCollection<TItem> items, int start, TKey key, Func<TItem, TKey> keyOf)
        where TKey : notnull
    {
        for (var index = start; index < items.Count; index++)
        {
            if (EqualityComparer<TKey>.Default.Equals(keyOf(items[index]), key))
            {
                return index;
            }
        }

        return -1;
    }
}
