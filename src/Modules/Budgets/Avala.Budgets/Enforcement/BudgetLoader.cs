using Avala.Agents.Contracts;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetLoader(BudgetBook book, IBudgetFiles files, IEventBus bus) : IHandle<SessionOpened>
{
    public async ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        var read = await files.ReadAsync(integrationEvent.WorkingDirectory, integrationEvent.Connection, cancellationToken);
        var budget = read.Caps.Match(
            found => found.Match(
                caps => new SessionBudget(integrationEvent.Session, BudgetFileStatus.Applied, Option<BudgetError>.None, caps, read.Origin),
                () => new SessionBudget(integrationEvent.Session, BudgetFileStatus.Absent, Option<BudgetError>.None, Breaches.Unlimited, read.Origin)),
            error => new SessionBudget(integrationEvent.Session, BudgetFileStatus.Rejected, error, Breaches.Unlimited, read.Origin));

        await book.KeepAsync(budget, integrationEvent.Connection, cancellationToken);
        await bus.PublishAsync(new BudgetLoaded(budget), cancellationToken);
    }
}
