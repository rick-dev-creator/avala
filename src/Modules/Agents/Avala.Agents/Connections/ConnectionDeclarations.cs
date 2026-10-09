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

    public DeclaredConnection Declared =>
        new(Name, Provider, Credential.Map(credential => credential.Source)) { Origin = Origin, Reference = Credential.Bind(credential => credential.Reference) };

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

internal sealed record ConnectionDeclarations(IReadOnlyList<ConnectionDeclaration> Connections, Option<ConnectionName> Fixed)
{
    public const string Auto = "auto";

    public static ConnectionDeclarations Nothing { get; } = new([], Option<ConnectionName>.None);

    public DefaultMode Mode => Fixed.IsSome ? DefaultMode.Fixed : DefaultMode.Auto;

    public Option<ConnectionName> Default =>
        Fixed.IsSome ? Fixed
        : Connections.Count == 0 ? Option<ConnectionName>.None
        : Connections[0].Name;

    public static ConnectionDeclarations Implicit(IEnumerable<IAgentProvider> providers) => Implicit(providers, []);

    public static ConnectionDeclarations Implicit(IEnumerable<IAgentProvider> providers, IReadOnlyList<ConnectionDeclaration> discovered) =>
        new(
            Distinct(providers
                .Select(provider => provider.Info)
                .DistinctBy(info => info.Id, StringComparer.Ordinal)
                .SelectMany(info => discovered.Any(found => found.Provider == info.Id)
                    ? discovered.Where(found => found.Provider == info.Id)
                    : info.OffersImplicitConnection
                    ? [new ConnectionDeclaration(new ConnectionName(info.Id), info.Id) { Origin = ConnectionOrigin.Implicit }]
                    : [])),
            Option<ConnectionName>.None);

    public static IReadOnlyList<ConnectionDeclaration> Distinct(IEnumerable<ConnectionDeclaration> connections) =>
        Fill([], connections);

    public Result<ConnectionDeclarations, ConnectionError> Merged(IEnumerable<IAgentProvider> providers, IReadOnlyList<ConnectionDeclaration> discovered)
    {
        var merged = Connections.Count == 0
            ? Implicit(providers, discovered) with { Fixed = Fixed }
            : this with { Connections = Fill(Connections, discovered) };

        return merged.Fixed.Match(
            named => merged.Connections.Any(connection => connection.Name == named)
                ? Result<ConnectionDeclarations, ConnectionError>.Success(merged)
                : ConnectionError.UnknownDefault,
            () => merged);
    }

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

        var name = requested.IsSome ? requested : Default;

        return Connections.FirstOrDefault(connection => Option<ConnectionName>.Some(connection.Name) == name) is { } found
            ? found
            : ConnectionError.UnknownConnection;
    }
}

internal interface IConnectionFile
{
    ValueTask<Result<Option<ConnectionDeclarations>, ConnectionError>> LoadAsync(CancellationToken cancellationToken);

    ValueTask<Result<ConnectionDeclarations, ConnectionError>> ChangeAsync(IConnectionChange change, CancellationToken cancellationToken);
}
