using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Sdk;
using Microsoft.EntityFrameworkCore;

namespace Avala.Budgets.Storage;

internal sealed class SqliteInterventionStore(AvalaPaths paths) : IInterventionStore, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly HashSet<int> written = [];
    private BudgetsDbContext? context;

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

    public Task<IReadOnlyList<BudgetIntervention>> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<BudgetIntervention>>(
            async database =>
            [
                .. (await database.Interventions.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .Where(row => !written.Contains(row.Key))
                    .Select(row => row.Intervention()),
            ],
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private Task<T> RunAsync<T>(Func<BudgetsDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<BudgetsDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new BudgetsDbContext(paths.Database("budgets"));
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        return context;
    }
}
