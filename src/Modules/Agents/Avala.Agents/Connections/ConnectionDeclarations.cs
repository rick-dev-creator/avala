using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Connections;

internal sealed record CredentialDeclaration(string Source, Option<string> Reference);

internal sealed record ConnectionDeclaration(ConnectionName Name, string Provider)
{
    public Option<CredentialDeclaration> Credential { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; } = ImmutableDictionary<string, string>.Empty;

    public DeclaredConnection Declared => new(Name, Provider, Credential.Map(credential => credential.Source));
}

internal sealed record ConnectionDeclarations(IReadOnlyList<ConnectionDeclaration> Connections, ConnectionName Default)
{
    public static ConnectionDeclarations Implicit(IEnumerable<IAgentProvider> providers)
    {
        var connections = providers
            .Select(provider => provider.Info.Id)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new ConnectionDeclaration(new ConnectionName(id), id))
            .ToList();

        return new ConnectionDeclarations(connections, connections.Count == 0 ? default : connections[0].Name);
    }

    public Result<ConnectionDeclaration, ConnectionError> Named(Option<ConnectionName> requested)
    {
        if (Connections.Count == 0)
        {
            return ConnectionError.NoConnections;
        }

        var name = requested.Match(chosen => chosen, () => Default);

        return Connections.FirstOrDefault(connection => connection.Name == name) is { } found
            ? found
            : ConnectionError.UnknownConnection;
    }
}

internal interface IConnectionFile
{
    ValueTask<Result<Option<ConnectionDeclarations>, ConnectionError>> LoadAsync(CancellationToken cancellationToken);
}
