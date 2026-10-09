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
}

public enum ConnectionFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public sealed record DeclaredConnection(ConnectionName Name, string Provider, Option<string> Source);

public sealed record ConnectionCatalog(
    ConnectionFileStatus File,
    Option<ConnectionError> Error,
    IReadOnlyList<DeclaredConnection> Connections,
    Option<ConnectionName> Default);

public sealed record ConnectionInfo(ConnectionName Name, ProviderInfo Provider);

public interface IConnections
{
    ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken);

    ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken);
}
