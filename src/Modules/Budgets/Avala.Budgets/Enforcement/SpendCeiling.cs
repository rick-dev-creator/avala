using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Admission;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class SpendCeiling(IMachineBudgetFile machine, IUsageHistory history, BudgetActions actions, TimeProvider clock)
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

        if (running.Count == 0)
        {
            return;
        }

        var budget = await machine.LoadAsync(cancellationToken);

        if (budget.Declaration.IsNone && !budget.Ceiled)
        {
            return;
        }

        var today = budget.Declaration.IsSome ? Option<UsagePeriod>.None : await TodayAsync(cancellationToken);

        foreach (var (job, connection) in running)
        {
            var breach = budget.Declaration.IsSome ? budget.Declaration : today.Bind(spent => budget.BreachBy(spent, connection));
            await breach.Match(found => actions.HoldAsync(job, found, cancellationToken), () => Task.CompletedTask);
        }
    }

    private Option<(JobId Job, ConnectionName Connection)> Running(JobId job) =>
        statuses.GetValueOrDefault(job) == JobStatus.Running && sessions.TryGetValue(job, out var session) && connections.TryGetValue(session, out var connection)
            ? (job, connection)
            : Option<(JobId, ConnectionName)>.None;

    private async Task<Option<UsagePeriod>> TodayAsync(CancellationToken cancellationToken)
    {
        var zone = clock.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

        return await history.DailyAsync(today, today, zone, cancellationToken) is [var period, ..] ? period : Option<UsagePeriod>.None;
    }
}
