using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Following;

namespace Avala.Workbench.Fleet;

internal sealed record ConnectionState(ConnectionName Name, string Provider, bool IsDefault, Option<AgentAccount> Account, Option<UsageSummary> Usage, IReadOnlyList<BoardJob> Agents);

internal sealed record FleetState(ConnectionFileStatus File, Option<ConnectionError> Error, IReadOnlyList<ConnectionState> Connections);

internal sealed class FleetReader(IConnections connections, IUsage usage, SessionBook sessions, JobBoard board)
{
    public async ValueTask<FleetState> ReadAsync(CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);
        var used = usage.ByConnection().ToDictionary(found => found.Connection);
        var declared = catalog.Connections.ToDictionary(connection => connection.Name);
        var agents = board.Jobs.Values
            .Where(job => job.Status.IsUnderway())
            .Select(job => (Job: job, Connection: ConnectionOf(job)))
            .Where(found => found.Connection.IsSome)
            .ToLookup(found => found.Connection.Match(name => name, () => default), found => found.Job);
        var names = catalog.Connections.Select(connection => connection.Name)
            .Concat(used.Keys.Where(name => !declared.ContainsKey(name)).OrderBy(name => name.Value, StringComparer.Ordinal));

        return new FleetState(
            catalog.File,
            catalog.Error,
            [
                .. names.Select(name => new ConnectionState(
                    name,
                    Provider(name, declared, used),
                    catalog.Default == Option<ConnectionName>.Some(name),
                    sessions.LatestOn(name).Bind(seen => seen.Account),
                    used.TryGetValue(name, out var reported) ? Option<UsageSummary>.Some(reported.Usage) : Option<UsageSummary>.None,
                    [.. agents[name].OrderBy(job => job.Summary.Submitted)])),
            ]);
    }

    private Option<ConnectionName> ConnectionOf(BoardJob job) =>
        sessions.LatestOf(job.Job).Match(seen => Option<ConnectionName>.Some(seen.Connection), () => job.Summary.Connection);

    private string Provider(ConnectionName name, Dictionary<ConnectionName, DeclaredConnection> declared, Dictionary<ConnectionName, ConnectionUsage> used) =>
        sessions.LatestOn(name).Match(
            seen => seen.Provider.Name,
            () => used.TryGetValue(name, out var reported) ? reported.Provider.Name : declared.TryGetValue(name, out var connection) ? connection.Provider : string.Empty);
}

internal static class JobProgress
{
    extension(JobStatus status)
    {
        public bool IsUnderway() => status is JobStatus.Preparing or JobStatus.Running or JobStatus.Checking;
    }
}
