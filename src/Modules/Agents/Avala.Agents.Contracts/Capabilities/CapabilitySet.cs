using System.Collections.Immutable;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Capabilities;

public sealed record CapabilitySet
{
    private readonly ImmutableDictionary<Type, ICapability> components;

    private CapabilitySet(ImmutableDictionary<Type, ICapability> components) => this.components = components;

    public static CapabilitySet None { get; } = new(ImmutableDictionary<Type, ICapability>.Empty);

    public IEnumerable<ICapability> Components => components.Values.OrderBy(component => component.GetType().Name, StringComparer.Ordinal);

    public static CapabilitySet Of(params ICapability[] components) => None.With(components);

    public CapabilitySet With(params ICapability[] attached) =>
        new(attached.Aggregate(components, (set, component) => set.SetItem(component.GetType(), component)));

    public CapabilitySet Without<T>()
        where T : ICapability =>
        new(components.Remove(typeof(T)));

    public Option<T> Get<T>()
        where T : class, ICapability =>
        components.TryGetValue(typeof(T), out var component) ? (T)component : Option<T>.None;

    public bool Has<T>()
        where T : ICapability =>
        components.ContainsKey(typeof(T));

    public bool Equals(CapabilitySet? other) =>
        other is not null
        && components.Count == other.components.Count
        && components.All(component => other.components.TryGetValue(component.Key, out var same) && same.Equals(component.Value));

    public override int GetHashCode() => components.Values.Aggregate(components.Count, (hash, component) => hash ^ component.GetHashCode());

    public override string ToString() => $"[{string.Join(", ", Components)}]";
}
