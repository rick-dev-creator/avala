using System.Collections.Immutable;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Connections;

public sealed record ConnectionEnvironment
{
    public static ConnectionEnvironment Default { get; } = new();

    public Option<string> ConfigurationDirectory { get; init; }

    public Option<Secret> ApiKey { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; } = ImmutableDictionary<string, string>.Empty;
}

public sealed record CredentialRequest(ConnectionName Connection, Option<string> Reference);

public interface ICredentialSource
{
    string Source { get; }

    ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken);
}
