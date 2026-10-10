using System.Collections;
using System.Collections.Immutable;

namespace Avala.Agents.Contracts.Capabilities;

public sealed record ValueList<T> : IReadOnlyList<T>
    where T : notnull
{
    private readonly ImmutableArray<T> items;

    public ValueList(IEnumerable<T> items) => this.items = [.. items.Distinct()];

    public int Count => items.Length;

    public T this[int index] => items[index];

    public bool Contains(T item) => items.Contains(item);

    public bool Equals(ValueList<T>? other) => other is not null && items.SequenceEqual(other.items);

    public override int GetHashCode() => items.Aggregate(items.Length, HashCode.Combine);

    public override string ToString() => $"[{string.Join(", ", items)}]";

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
