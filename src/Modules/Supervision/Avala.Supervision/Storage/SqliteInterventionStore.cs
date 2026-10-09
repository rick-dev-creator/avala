using Avala.Sdk;
using Avala.Storage;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;
using Microsoft.EntityFrameworkCore;

namespace Avala.Supervision.Storage;

internal sealed class SqliteInterventionStore(AvalaPaths paths) : IInterventionStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<SupervisionDbContext> owner = new(paths.Database("supervision"), file => new SupervisionDbContext(file));
    private readonly HashSet<int> written = [];

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

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<T> RunAsync<T>(Func<SupervisionDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
