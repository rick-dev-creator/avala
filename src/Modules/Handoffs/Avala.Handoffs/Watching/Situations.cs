using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Watching;

internal sealed record JobSituation(JobHistory History, Situation Situation)
{
    public JobId Job => History.Summary.Job;

    public ConnectionName Connection => Situation.Own.Connection;
}

internal sealed class JobPlaces(IJobCatalog catalog, IWorkspaces workspaces, ILimitRules rules)
{
    public async Task<Option<(JobHistory History, LimitRules Rules)>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            async history => Option<(JobHistory, LimitRules)>.Some((history, await RulesOfAsync(history, cancellationToken))),
            () => Task.FromResult(Option<(JobHistory, LimitRules)>.None));

    private async Task<LimitRules> RulesOfAsync(JobHistory history, CancellationToken cancellationToken) =>
        await history.Summary.Workspace.Match(
            async id => await (await workspaces.FindAsync(id, cancellationToken)).Match(
                found => rules.OfWorktreeAsync(found.Path, cancellationToken).AsTask(),
                _ => Task.FromResult(LimitRules.Default)),
            () => Task.FromResult(LimitRules.Default));
}

internal sealed class ConnectionReadings(IConnections connections, IUsage usage)
{
    public async Task<(IReadOnlyList<ConnectionState> Usable, IReadOnlyList<DeclaredConnection> Known)> ReadAsync(CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);
        var usable = new List<ConnectionState>();

        foreach (var declared in catalog.Connections)
        {
            if ((await connections.CheckAsync(declared.Name, cancellationToken)).IsSuccess)
            {
                usable.Add(StateOf(declared.Name, declared.Provider));
            }
        }

        return (usable, catalog.Connections);
    }

    public ConnectionState StateOf(ConnectionName connection, string provider) =>
        new(connection, provider, [.. usage.ByConnection().Where(used => used.Connection == connection).SelectMany(used => used.Usage.Limits)]);

    public string ProviderOf(ConnectionName connection, IReadOnlyList<DeclaredConnection> known) =>
        known.FirstOrDefault(declared => declared.Name == connection)?.Provider
        ?? usage.ByConnection().FirstOrDefault(used => used.Connection == connection)?.Provider.Id
        ?? string.Empty;
}

internal sealed class Situations(JobPlaces places, ConnectionReadings readings, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<Option<JobSituation>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        await (await places.OfAsync(job, cancellationToken)).Match(
            found => found.History.Summary.Connection.Match(
                connection => SituatedAsync(found.History, found.Rules, connection, cancellationToken),
                () => Task.FromResult(Option<JobSituation>.None)),
            () => Task.FromResult(Option<JobSituation>.None));

    private async Task<Option<JobSituation>> SituatedAsync(JobHistory history, LimitRules rules, ConnectionName connection, CancellationToken cancellationToken)
    {
        var (usable, known) = await readings.ReadAsync(cancellationToken);
        var own = readings.StateOf(connection, readings.ProviderOf(connection, known));

        return new JobSituation(history, new Situation(own, history.Choice, rules, usable, clock.GetUtcNow()));
    }
}
