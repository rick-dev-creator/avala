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

internal sealed class BudgetEnforcer(BudgetBook book, IUsage usage, IEnumerable<IResources> resources, BudgetActions actions)
    : IHandle<BudgetLoaded>, IHandle<JobSessionStarted>, IHandle<JobProgressed>, IHandle<UsageRecorded>, IHandle<ResourcesSampled>, IHandle<JobSubmitted>
{
    private readonly Dictionary<JobId, SessionId> sessions = [];
    private readonly Dictionary<JobId, JobStatus> statuses = [];

    private Lineage Tree => new(book.Carves, Spent, Ended);

    public async ValueTask HandleAsync(JobSubmitted integrationEvent, CancellationToken cancellationToken) =>
        await integrationEvent.Parent.Match(
            parent => actions.CarveAsync(
                AllowanceOf(parent).CarveFor(parent, integrationEvent.Job, Tree.CommittedBy(parent), actions.Now),
                cancellationToken),
            () => Task.CompletedTask);

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

    public async ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in integrationEvent.Job.Match(LineOf, () => []))
        {
            await EnforceAsync(job, cancellationToken);
        }
    }

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
            .Bind(budgeted => (budgeted.Budget with { Caps = budgeted.Budget.Caps.Within(book.CarveOf(job)) }).BreachBy(
                Tree.CommittedBy(job),
                LimitsOf(budgeted.Connection),
                resources.Sum(measured => measured.OfJob(job).MemoryBytes)))
            .Match(breach => actions.HoldAsync(job, breach, cancellationToken), () => Task.CompletedTask);
    }

    private BudgetCaps AllowanceOf(JobId job)
    {
        var own = sessions.TryGetValue(job, out var session)
            ? book.Budgeted(session).Match(budgeted => budgeted.Budget.Caps, () => Breaches.Unlimited)
            : Breaches.Unlimited;

        return own.Within(book.CarveOf(job));
    }

    private IReadOnlyList<JobId> LineOf(JobId job) =>
        [job, .. book.CarveOf(job).Match(carve => LineOf(carve.Parent), () => [])];

    private Commitment Spent(JobId job) => usage.OfJob(job).Match(Commitment.Of, () => Commitment.Nothing);

    private bool Ended(JobId job) => statuses.GetValueOrDefault(job) is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed;

    private IReadOnlyList<UsageLimit> LimitsOf(ConnectionName connection) =>
        [
            .. usage.ByConnection()
                .Where(used => used.Connection == connection)
                .SelectMany(used => used.Usage.Limits)
                .Where(limit => limit.ResetsAt.Match(resets => resets > actions.Now, () => true)),
        ];
}
