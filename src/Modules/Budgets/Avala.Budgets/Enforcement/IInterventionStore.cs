using Avala.Budgets.Contracts;

namespace Avala.Budgets.Enforcement;

internal interface IInterventionStore
{
    Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken);

    Task RecordAsync(BudgetCarve carve, CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetCarve>> EarlierCarvesAsync(CancellationToken cancellationToken);
}
