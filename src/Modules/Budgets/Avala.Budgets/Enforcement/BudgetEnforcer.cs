using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Caps;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetEnforcer(BudgetBook book, IUsage usage, BudgetHolds holds)
    : IHandle<JobSessionStarted>, IHandle<JobProgressed>, IHandle<UsageRecorded>
{
    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        book.Tie(integrationEvent.Job, integrationEvent.Session);
        await EnforceAsync(integrationEvent.Job, cancellationToken);
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        book.Track(integrationEvent.Job, integrationEvent.Status);
        await EnforceAsync(integrationEvent.Job, cancellationToken);
    }

    public async ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken) =>
        await integrationEvent.Job.Match(job => EnforceAsync(job, cancellationToken), () => Task.CompletedTask);

    private async Task EnforceAsync(JobId job, CancellationToken cancellationToken)
    {
        if (!book.IsRunning(job))
        {
            return;
        }

        await book.BudgetOfJob(job)
            .Bind(budgeted =>
            {
                var spent = usage.OfJob(job).Match(summary => summary, () => Breaches.NothingSpent);

                return budgeted.Budget.BreachBy(spent, LimitsOf(budgeted.Provider));
            })
            .Match(breach => holds.HoldAsync(job, breach, cancellationToken), () => Task.CompletedTask);
    }

    private IReadOnlyList<UsageLimit> LimitsOf(ProviderInfo provider) =>
        [.. usage.ByProvider().Where(used => used.Provider == provider).SelectMany(used => used.Usage.Limits)];
}
