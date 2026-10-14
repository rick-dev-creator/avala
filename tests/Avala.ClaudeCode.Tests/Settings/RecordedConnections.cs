using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.ClaudeCode.Tests.Settings;

internal sealed class RecordedConnections : IConnections
{
    public List<DeclaredConnection> Connections { get; } = [];

    public List<(Option<ConnectionName> Replacing, ConnectionEdit Edit)> Declared { get; } = [];

    public Option<ConnectionError> Refusal { get; set; }

    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ConnectionCatalog(ConnectionFileStatus.Applied, Option<ConnectionError>.None, [.. Connections], Option<ConnectionName>.None));

    public ValueTask<ConnectionCatalog> RediscoverAsync(CancellationToken cancellationToken) => CatalogAsync(cancellationToken);

    public ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ConnectionInfo, ConnectionError>.Failure(ConnectionError.UnknownConnection));

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ConnectionCatalog, ConnectionError>.Failure(ConnectionError.UnknownConnection));

    public async ValueTask<Result<ConnectionCatalog, ConnectionError>> DeclareAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken)
    {
        Declared.Add((replacing, connection));

        var catalog = await CatalogAsync(cancellationToken);

        return Refusal.Match(Result<ConnectionCatalog, ConnectionError>.Failure, () => Result<ConnectionCatalog, ConnectionError>.Success(catalog));
    }

    public ValueTask<Result<CapabilitySet, ConnectionError>> CapabilitiesAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<CapabilitySet, ConnectionError>.Success(CapabilitySet.None));

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> RemoveAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ConnectionCatalog, ConnectionError>.Failure(ConnectionError.UnknownConnection));
}
