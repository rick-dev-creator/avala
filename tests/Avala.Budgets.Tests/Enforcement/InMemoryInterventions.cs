using System.Collections.Concurrent;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;

namespace Avala.Budgets.Tests.Enforcement;

internal sealed class InMemoryInterventions : IInterventionStore
{
    private readonly ConcurrentQueue<BudgetIntervention> recorded = new();
    private readonly ConcurrentQueue<BudgetCarve> carves = new();
    private readonly ConcurrentQueue<BudgetedSession> budgets = new();

    public IReadOnlyList<BudgetIntervention> Recorded => [.. recorded];

    public IReadOnlyList<BudgetCarve> Carves => [.. carves];

    public IReadOnlyList<BudgetIntervention> Earlier { get; set; } = [];

    public IReadOnlyList<BudgetCarve> EarlierCarves { get; set; } = [];

    public IReadOnlyList<BudgetedSession> Budgets => [.. budgets];

    public IReadOnlyList<BudgetedSession> EarlierBudgets { get; set; } = [];

    public Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken)
    {
        recorded.Enqueue(intervention);

        return Task.CompletedTask;
    }

    public Task RecordAsync(BudgetCarve carve, CancellationToken cancellationToken)
    {
        carves.Enqueue(carve);

        return Task.CompletedTask;
    }

    public Task RecordAsync(BudgetedSession budgeted, CancellationToken cancellationToken)
    {
        budgets.Enqueue(budgeted);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BudgetedSession>> EarlierBudgetsAsync(CancellationToken cancellationToken) => Task.FromResult(EarlierBudgets);

    public Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);

    public Task<IReadOnlyList<BudgetCarve>> EarlierCarvesAsync(CancellationToken cancellationToken) => Task.FromResult(EarlierCarves);
}
