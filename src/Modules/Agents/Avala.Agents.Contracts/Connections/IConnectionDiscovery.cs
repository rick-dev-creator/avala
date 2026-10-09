using System.Collections.Immutable;

namespace Avala.Agents.Contracts.Connections;

public sealed record CredentialReference(string Source, string Reference);

public sealed record DiscoveredConnection(ConnectionName Name, string Provider, CredentialReference Credential)
{
    public IReadOnlyDictionary<string, string> Settings { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public interface IConnectionDiscovery
{
    ValueTask<IReadOnlyList<DiscoveredConnection>> DiscoverAsync(CancellationToken cancellationToken);
}
