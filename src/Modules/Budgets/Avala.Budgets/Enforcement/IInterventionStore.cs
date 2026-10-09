using Avala.Agents.Contracts.Connections;
using Avala.Budgets.Contracts;

namespace Avala.Budgets.Enforcement;

internal sealed record BudgetedSession(SessionBudget Budget, ConnectionName Connection);

internal interface IInterventionStore
{
    Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken);

    Task RecordAsync(BudgetCarve carve, CancellationToken cancellationToken);

    Task RecordAsync(BudgetedSession budgeted, CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetCarve>> EarlierCarvesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetedSession>> EarlierBudgetsAsync(CancellationToken cancellationToken);
}
