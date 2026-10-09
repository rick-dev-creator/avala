using System.Collections.Concurrent;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;

namespace Avala.Budgets.Tests.Enforcement;

internal sealed class InMemoryInterventions : IInterventionStore
{
    private readonly ConcurrentQueue<BudgetIntervention> recorded = new();

    public IReadOnlyList<BudgetIntervention> Recorded => [.. recorded];

    public IReadOnlyList<BudgetIntervention> Earlier { get; set; } = [];

    public Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken)
    {
        recorded.Enqueue(intervention);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);
}
