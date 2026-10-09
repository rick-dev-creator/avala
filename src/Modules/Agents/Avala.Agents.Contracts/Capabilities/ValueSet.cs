using System.Collections;
using System.Collections.Immutable;

namespace Avala.Agents.Contracts.Capabilities;

public sealed record ValueSet<T> : IReadOnlyCollection<T>
    where T : notnull
{
    private readonly ImmutableSortedSet<T> items;

    public ValueSet(IEnumerable<T> items) =>
        this.items = ImmutableSortedSet.CreateRange(typeof(T) == typeof(string) ? (IComparer<T>)StringComparer.Ordinal : Comparer<T>.Default, items);

    public int Count => items.Count;

    public bool Contains(T item) => items.Contains(item);

    public bool Equals(ValueSet<T>? other) => other is not null && items.SetEquals(other.items);

    public override int GetHashCode() => items.Aggregate(items.Count, HashCode.Combine);

    public override string ToString() => $"[{string.Join(", ", items)}]";

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
