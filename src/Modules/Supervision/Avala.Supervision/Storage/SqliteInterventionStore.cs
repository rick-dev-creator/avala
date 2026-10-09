using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;
using Microsoft.EntityFrameworkCore;

namespace Avala.Supervision.Storage;

internal sealed class SqliteInterventionStore(AvalaPaths paths) : IInterventionStore, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly HashSet<int> written = [];
    private SupervisionDbContext? context;

    public Task RecordAsync(SupervisionIntervention intervention, CancellationToken cancellationToken) =>
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

    public Task<IReadOnlyList<SupervisionIntervention>> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<SupervisionIntervention>>(
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

    private Task<T> RunAsync<T>(Func<SupervisionDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<SupervisionDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new SupervisionDbContext(paths.Database("supervision"));
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        return context;
    }
}
