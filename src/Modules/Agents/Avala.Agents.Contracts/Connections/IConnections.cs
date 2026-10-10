using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Connections;

public enum ConnectionError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidName,
    DuplicateName,
    MissingProvider,
    MissingSource,
    UnknownDefault,
    NoConnections,
    UnknownConnection,
    UnknownProvider,
    UnknownSource,
    MissingReference,
    MissingVariable,
    MissingFolder,
    Unwritable,
    RemovesTheDefault,
    UnofferedModel,
    UnofferedEffort,
}

public enum ConnectionFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum ConnectionOrigin
{
    Declared,
    Discovered,
    Implicit,
}

public enum DefaultMode
{
    Auto,
    Fixed,
}

public sealed record DeclaredConnection(ConnectionName Name, string Provider, Option<string> Source)
{
    public ConnectionOrigin Origin { get; init; }

    public Option<string> Reference { get; init; }

    public Option<ConnectionError> Problem { get; init; }
}

public sealed record ConnectionCatalog(
    ConnectionFileStatus File,
    Option<ConnectionError> Error,
    IReadOnlyList<DeclaredConnection> Connections,
    Option<ConnectionName> Default)
{
    public DefaultMode DefaultMode { get; init; }

    public IReadOnlyList<ProviderInfo> Providers { get; init; } = [];

    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record ConnectionEdit(ConnectionName Name, string Provider)
{
    public Option<CredentialReference> Credential { get; init; }

    public Option<ModelChoice> Model { get; init; }
}

public sealed record ConnectionInfo(ConnectionName Name, ProviderInfo Provider)
{
    public CapabilitySet Capabilities { get; init; } = CapabilitySet.None;
}

public interface IConnections
{
    ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken);

    ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken);

    ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(Option<ConnectionName> connection, CancellationToken cancellationToken);

    ValueTask<Result<ConnectionCatalog, ConnectionError>> DeclareAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken);

    ValueTask<Result<CapabilitySet, ConnectionError>> CapabilitiesAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken);

    ValueTask<Result<ConnectionCatalog, ConnectionError>> RemoveAsync(ConnectionName connection, CancellationToken cancellationToken);
}
