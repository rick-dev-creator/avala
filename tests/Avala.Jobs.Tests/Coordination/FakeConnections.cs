using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Launching;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class FakeConnections : IConnections
{
    public static ProviderInfo Provider { get; } = new("agent", "Agent");

    public Dictionary<string, ConnectionError> Refused { get; } = new(StringComparer.Ordinal);

    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ConnectionCatalog(ConnectionFileStatus.Absent, Option<ConnectionError>.None, [], Option<ConnectionName>.None));

    public ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken)
    {
        var name = connection.Match(named => named, () => FakeAgents.DefaultConnection);

        return ValueTask.FromResult(Refused.TryGetValue(name.Value, out var error)
            ? Result<ConnectionInfo, ConnectionError>.Failure(error)
            : Result<ConnectionInfo, ConnectionError>.Success(new ConnectionInfo(name, Provider)));
    }
}

internal sealed class FakeRepositoryDefaults : IRepositoryDefaults
{
    public Result<Option<ConnectionName>, JobRejection> Connection { get; set; } = Option<ConnectionName>.None;

    public List<string> Read { get; } = [];

    public ValueTask<Result<Option<ConnectionName>, JobRejection>> ConnectionAsync(string worktree, CancellationToken cancellationToken)
    {
        Read.Add(worktree);

        return ValueTask.FromResult(Connection);
    }
}
