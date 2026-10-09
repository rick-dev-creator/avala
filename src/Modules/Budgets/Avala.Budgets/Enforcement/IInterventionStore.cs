using Avala.Budgets.Contracts;

namespace Avala.Budgets.Enforcement;

internal interface IInterventionStore
{
    Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken);
}
