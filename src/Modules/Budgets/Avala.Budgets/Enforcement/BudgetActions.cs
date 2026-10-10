using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetActions(BudgetBook book, IJobs jobs, IEventBus bus, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task HoldAsync(JobId job, BudgetBreach breach, CancellationToken cancellationToken)
    {
        if (!(await jobs.HoldAsync(job, breach.Reason, cancellationToken)).TryGetValue(out var hold, out _))
        {
            return;
        }

        await RecordAsync(hold, breach, cancellationToken);
    }

    public async Task RecordAsync(JobHold hold, BudgetBreach breach, CancellationToken cancellationToken)
    {
        var intervention = new BudgetIntervention(hold, breach, clock.GetUtcNow());
        await book.RecordAsync(intervention, cancellationToken);
        await bus.PublishAsync(new BudgetIntervened(intervention), cancellationToken);
    }

    public async Task CarveAsync(BudgetCarve carve, CancellationToken cancellationToken)
    {
        await book.RecordAsync(carve, cancellationToken);
        await bus.PublishAsync(new BudgetCarved(carve), cancellationToken);
    }
}
