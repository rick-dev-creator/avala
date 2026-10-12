using Avala.Sdk;

namespace Avala.Rendering.Caching;

internal sealed class RecentlyUsed<TValue>(long budget, Func<TValue, long> size, Action<TValue> evict)
    where TValue : notnull
{
    private readonly Dictionary<string, LinkedListNode<(string Key, TValue Value)>> entries = [];
    private readonly LinkedList<(string Key, TValue Value)> recent = [];

    public IReadOnlyList<TValue> Kept => [.. recent.Select(entry => entry.Value)];

    public Option<TValue> Find(string key)
    {
        if (!entries.TryGetValue(key, out var found))
        {
            return Option<TValue>.None;
        }

        recent.Remove(found);
        recent.AddFirst(found);

        return found.Value.Value;
    }

    public TValue Keep(string key, TValue value)
    {
        entries[key] = recent.AddFirst((key, value));
        var bytes = recent.Sum(entry => size(entry.Value));

        while (bytes > budget && recent.Last is { } oldest && oldest != recent.First)
        {
            bytes -= size(oldest.Value.Value);
            recent.RemoveLast();
            entries.Remove(oldest.Value.Key);
            evict(oldest.Value.Value);
        }

        return value;
    }
}
