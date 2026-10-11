using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class SpendCeiling(DailyCeiling ceiling, BudgetActions actions)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<JobProgressed>, IHandle<UsageRecorded>
{
    private readonly Dictionary<SessionId, ConnectionName> connections = [];
    private readonly Dictionary<JobId, SessionId> sessions = [];
    private readonly Dictionary<JobId, JobStatus> statuses = [];

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        connections[integrationEvent.Session] = integrationEvent.Connection;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Job] = integrationEvent.Session;
        await EnforceAsync([integrationEvent.Job], cancellationToken);
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        statuses[integrationEvent.Job] = integrationEvent.Status;
        await EnforceAsync([integrationEvent.Job], cancellationToken);
    }

    public async ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken) =>
        await EnforceAsync([.. statuses.Keys], cancellationToken);

    private async Task EnforceAsync(IReadOnlyList<JobId> jobs, CancellationToken cancellationToken)
    {
        var running = jobs.SelectMany(job => Running(job).Match<(JobId Job, ConnectionName Connection)[]>(tied => [tied], () => [])).ToList();

        foreach (var (job, breach) in await ceiling.BreachesAsync(running, cancellationToken))
        {
            await actions.HoldRunningAsync(job, breach, cancellationToken);
        }
    }

    private Option<(JobId Job, ConnectionName Connection)> Running(JobId job) =>
        statuses.GetValueOrDefault(job) == JobStatus.Running && sessions.TryGetValue(job, out var session) && connections.TryGetValue(session, out var connection)
            ? (job, connection)
            : Option<(JobId, ConnectionName)>.None;
}
