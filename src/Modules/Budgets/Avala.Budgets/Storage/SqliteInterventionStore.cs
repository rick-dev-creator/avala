using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Budgets.Storage;

internal sealed class SqliteInterventionStore(AvalaPaths paths) : IInterventionStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<BudgetsDbContext> owner = new(paths.Database("budgets"), file => new BudgetsDbContext(file));
    private readonly HashSet<int> written = [];
    private readonly HashSet<int> carved = [];
    private readonly HashSet<int> budgets = [];

    public Task RecordAsync(BudgetCarve carve, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredCarve.Of(carve);
                await database.Carves.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                carved.Add(row.Key);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredIntervention.Of(intervention);
                await database.Interventions.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                written.Add(row.Key);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task RecordAsync(BudgetedSession budgeted, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredSessionBudget.Of(budgeted);
                await database.SessionBudgets.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                budgets.Add(row.Key);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<BudgetedSession>> EarlierBudgetsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<BudgetedSession>>(
            async database =>
            [
                .. (await database.SessionBudgets.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !budgets.Contains(row.Key))
                    .Select(row => row.Budgeted()),
            ],
            cancellationToken);

    public Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<BudgetIntervention>>(
            async database =>
            [
                .. (await database.Interventions.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !written.Contains(row.Key))
                    .Select(row => row.Intervention()),
            ],
            cancellationToken);

    public Task<IReadOnlyList<BudgetCarve>> EarlierCarvesAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<BudgetCarve>>(
            async database =>
            [
                .. (await database.Carves.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !carved.Contains(row.Key))
                    .Select(row => row.Carve()),
            ],
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<T> RunAsync<T>(Func<BudgetsDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
