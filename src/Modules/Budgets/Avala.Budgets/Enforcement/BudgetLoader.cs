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
        var budget = (await files.ReadAsync(integrationEvent.WorkingDirectory, cancellationToken)).Match(
            found => found.Match(
                caps => new SessionBudget(integrationEvent.Session, BudgetFileStatus.Applied, Option<BudgetError>.None, caps),
                () => new SessionBudget(integrationEvent.Session, BudgetFileStatus.Absent, Option<BudgetError>.None, Breaches.Unlimited)),
            error => new SessionBudget(integrationEvent.Session, BudgetFileStatus.Rejected, error, Breaches.Unlimited));

        book.Keep(budget, integrationEvent.Provider);
        await bus.PublishAsync(new BudgetLoaded(budget), cancellationToken);
    }
}
