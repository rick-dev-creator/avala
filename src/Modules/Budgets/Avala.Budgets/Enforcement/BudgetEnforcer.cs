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

internal sealed class BudgetEnforcer(BudgetBook book, JobBreaches breaches, IEnumerable<IResources> resources, BudgetActions actions)
    : IHandle<BudgetLoaded>, IHandle<JobSessionStarted>, IHandle<JobProgressed>, IHandle<UsageRecorded>, IHandle<ResourcesSampled>, IHandle<JobSubmitted>, IHandle<JobHeld>
{
    private readonly Dictionary<JobId, SessionId> sessions = [];
    private readonly Dictionary<JobId, JobStatus> statuses = [];
    private readonly List<(JobId Child, JobId Parent)> uncarved = [];

    public async ValueTask HandleAsync(JobSubmitted integrationEvent, CancellationToken cancellationToken)
    {
        uncarved.AddRange(integrationEvent.Parent.Match<IEnumerable<(JobId, JobId)>>(parent => [(integrationEvent.Job, parent)], () => []));
        await CarveKnownAsync(cancellationToken);
    }

    public async ValueTask HandleAsync(BudgetLoaded integrationEvent, CancellationToken cancellationToken)
    {
        await CarveKnownAsync(cancellationToken);
        foreach (var job in sessions.Where(tied => tied.Value == integrationEvent.Budget.Session).Select(tied => tied.Key).ToList())
        {
            await EnforceAsync(job, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Job] = integrationEvent.Session;
        await CarveKnownAsync(cancellationToken);
        await EnforceAsync(integrationEvent.Job, cancellationToken);
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        statuses[integrationEvent.Job] = integrationEvent.Status;
        book.KeepStatus(integrationEvent.Job, integrationEvent.Status);
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

    public async ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken)
    {
        var hold = integrationEvent.Hold;

        if (hold.Reason == HoldReason.BudgetExceeded && !book.OfJob(hold.Job).Any(recorded => recorded.Hold == hold))
        {
            var breach = sessions.TryGetValue(hold.Job, out var session) ? breaches.Overspent(hold.Job, session) : book.Overspent(hold.Job);
            await breach.Match(found => actions.RecordAsync(hold, found, cancellationToken), () => Task.CompletedTask);
        }
    }

    private async Task CarveKnownAsync(CancellationToken cancellationToken) =>
        await uncarved
            .Select(waiting => AllowanceOf(waiting.Parent).Map(allowance => (waiting.Child, waiting.Parent, Allowance: allowance)))
            .FirstOrDefault(known => known.IsSome)
            .Match(known => CarveAsync(known.Child, known.Parent, known.Allowance, cancellationToken), () => Task.CompletedTask);

    private async Task CarveAsync(JobId child, JobId parent, BudgetCaps allowance, CancellationToken cancellationToken)
    {
        uncarved.Remove((child, parent));
        await actions.CarveAsync(allowance.CarveFor(parent, child, breaches.Tree.CommittedBy(parent), actions.Now), cancellationToken);
        foreach (var job in LineOf(child))
        {
            await EnforceAsync(job, cancellationToken);
        }

        await CarveKnownAsync(cancellationToken);
    }

    private async Task EnforceAsync(JobId job, CancellationToken cancellationToken)
    {
        var status = statuses.GetValueOrDefault(job);

        if (status is not (JobStatus.Running or JobStatus.Checking or JobStatus.AwaitingReview) || !sessions.TryGetValue(job, out var session))
        {
            return;
        }

        var breach = book.Budgeted(session).Bind(budgeted => breaches.Of(
            job,
            session,
            breaches.LimitsOf(budgeted.Connection, actions.Now),
            resources.Sum(measured => measured.OfJob(job).MemoryBytes)));
        var overspent = JobBreaches.Overspending(breach);
        book.KeepSpending(job, overspent);

        await (status == JobStatus.Running ? breach : overspent)
            .Match(found => actions.HoldAsync(job, found, cancellationToken), () => Task.CompletedTask);
    }

    private Option<BudgetCaps> AllowanceOf(JobId job) =>
        sessions.TryGetValue(job, out var session) && !uncarved.Exists(waiting => waiting.Child == job)
            ? book.Budgeted(session).Map(budgeted => budgeted.Budget.Caps.Within(book.CarveOf(job)))
            : Option<BudgetCaps>.None;

    private IReadOnlyList<JobId> LineOf(JobId job) =>
        [job, .. book.CarveOf(job).Match(carve => LineOf(carve.Parent), () => [])];
}
