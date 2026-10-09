using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetEnforcer(BudgetBook book, IUsage usage, IEnumerable<IResources> resources, BudgetHolds holds)
    : IHandle<BudgetLoaded>, IHandle<JobSessionStarted>, IHandle<JobProgressed>, IHandle<UsageRecorded>, IHandle<ResourcesSampled>
{
    private readonly Dictionary<JobId, SessionId> sessions = [];
    private readonly Dictionary<JobId, JobStatus> statuses = [];

    public async ValueTask HandleAsync(BudgetLoaded integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in sessions.Where(tied => tied.Value == integrationEvent.Budget.Session).Select(tied => tied.Key).ToList())
        {
            await EnforceAsync(job, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Job] = integrationEvent.Session;
        await EnforceAsync(integrationEvent.Job, cancellationToken);
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        statuses[integrationEvent.Job] = integrationEvent.Status;
        await EnforceAsync(integrationEvent.Job, cancellationToken);
    }

    public async ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken) =>
        await integrationEvent.Job.Match(job => EnforceAsync(job, cancellationToken), () => Task.CompletedTask);

    public async ValueTask HandleAsync(ResourcesSampled integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in statuses.Where(known => known.Value == JobStatus.Running).Select(known => known.Key).ToList())
        {
            await EnforceAsync(job, cancellationToken);
        }
    }

    private async Task EnforceAsync(JobId job, CancellationToken cancellationToken)
    {
        if (statuses.GetValueOrDefault(job) != JobStatus.Running || !sessions.TryGetValue(job, out var session))
        {
            return;
        }

        await book.Budgeted(session)
            .Bind(budgeted =>
            {
                var spent = usage.OfJob(job).Match(summary => summary, () => Breaches.NothingSpent);

                return budgeted.Budget.BreachBy(spent, LimitsOf(budgeted.Connection), resources.Sum(measured => measured.OfJob(job).MemoryBytes));
            })
            .Match(breach => holds.HoldAsync(job, breach, cancellationToken), () => Task.CompletedTask);
    }

    private IReadOnlyList<UsageLimit> LimitsOf(ConnectionName connection) =>
        [
            .. usage.ByConnection()
                .Where(used => used.Connection == connection)
                .SelectMany(used => used.Usage.Limits)
                .Where(limit => limit.ResetsAt.Match(resets => resets > holds.Now, () => true)),
        ];
}
