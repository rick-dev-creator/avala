using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetHolds(BudgetBook book, IJobs jobs, IEventBus bus, TimeProvider clock)
{
    public async Task HoldAsync(JobId job, BudgetBreach breach, CancellationToken cancellationToken)
    {
        if (!(await jobs.HoldAsync(job, breach.Reason, cancellationToken)).TryGetValue(out var hold, out _))
        {
            return;
        }

        var intervention = new BudgetIntervention(hold, breach, clock.GetUtcNow());
        await book.RecordAsync(intervention, cancellationToken);
        await bus.PublishAsync(new BudgetIntervened(intervention), cancellationToken);
    }
}
