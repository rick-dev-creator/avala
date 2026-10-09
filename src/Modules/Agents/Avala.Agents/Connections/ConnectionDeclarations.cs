using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Connections;

internal sealed record CredentialDeclaration(string Source, Option<string> Reference);

internal sealed record ConnectionDeclaration(ConnectionName Name, string Provider)
{
    public const int LongestName = 64;

    public Option<CredentialDeclaration> Credential { get; init; }

    public IReadOnlyDictionary<string, string> Settings { get; init; } = ImmutableDictionary<string, string>.Empty;

    public ConnectionOrigin Origin { get; init; }

    public DeclaredConnection Declared => new(Name, Provider, Credential.Map(credential => credential.Source)) { Origin = Origin };

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= LongestName
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public static ConnectionDeclaration From(DiscoveredConnection discovered) =>
        new(discovered.Name, discovered.Provider)
        {
            Credential = new CredentialDeclaration(discovered.Credential.Source, discovered.Credential.Reference),
            Settings = discovered.Settings,
            Origin = ConnectionOrigin.Discovered,
        };

    public bool Overlaps(ConnectionDeclaration other) =>
        Name == other.Name
        || (Provider == other.Provider && Credential.Bind(credential => credential.Reference).IsSome && Credential == other.Credential);
}

internal sealed record ConnectionDeclarations(IReadOnlyList<ConnectionDeclaration> Connections, ConnectionName Default)
{
    public static ConnectionDeclarations Implicit(IEnumerable<IAgentProvider> providers) => Implicit(providers, []);

    public static ConnectionDeclarations Implicit(IEnumerable<IAgentProvider> providers, IReadOnlyList<ConnectionDeclaration> discovered)
    {
        var connections = Distinct(providers
            .Select(provider => provider.Info.Id)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(id => discovered.Any(found => found.Provider == id)
                ? discovered.Where(found => found.Provider == id)
                : [new ConnectionDeclaration(new ConnectionName(id), id) { Origin = ConnectionOrigin.Implicit }]));

        return new ConnectionDeclarations(connections, connections.Count == 0 ? default : connections[0].Name);
    }

    public static IReadOnlyList<ConnectionDeclaration> Distinct(IEnumerable<ConnectionDeclaration> connections) =>
        Fill([], connections);

    public ConnectionDeclarations With(IReadOnlyList<ConnectionDeclaration> discovered) =>
        this with { Connections = Fill(Connections, discovered) };

    private static ImmutableList<ConnectionDeclaration> Fill(IEnumerable<ConnectionDeclaration> kept, IEnumerable<ConnectionDeclaration> candidates) =>
        candidates.Aggregate(
            kept.ToImmutableList(),
            (taken, connection) => taken.Any(earlier => earlier.Overlaps(connection)) ? taken : taken.Add(connection));

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
