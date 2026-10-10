using Avala.Sdk;
using Avala.Storage;
using Avala.Triggers.Contracts;
using Avala.Triggers.Firing;
using Avala.Triggers.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Avala.Triggers.Storage;

internal sealed class SqliteTriggerStore(AvalaPaths paths) : IScheduleStore, IRunStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<TriggersDbContext> owner = new(paths.Database("triggers"), file => new TriggersDbContext(file));

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<IReadOnlyList<ScheduleState>> SchedulesAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<ScheduleState>>(
            async database => [.. (await database.Schedules.AsNoTracking().ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepAsync(ScheduleState state, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredSchedule.Of(state);

                if (await database.Schedules.FindAsync([row.Trigger], cancellationToken) is { } stored)
                {
                    database.Entry(stored).CurrentValues.SetValues(row);
                }
                else
                {
                    await database.Schedules.AddAsync(row, cancellationToken);
                }

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task<IReadOnlyList<TriggerRun>> RunsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<TriggerRun>>(
            async database => [.. (await database.Runs.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepRunAsync(TriggerRun run, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredRun.Of(run);

                if (await database.Runs.FirstOrDefaultAsync(stored => stored.Run == run.Id, cancellationToken) is { } stored)
                {
                    stored.Fact = row.Fact;
                }
                else
                {
                    await database.Runs.AddAsync(row, cancellationToken);
                }

                var saved = await SaveAsync(database, cancellationToken);
                await PruneAsync(database.Runs, RunJournal.RunsKept, cancellationToken);

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<WebhookDelivery>> DeliveriesAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<WebhookDelivery>>(
            async database => [.. (await database.Deliveries.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task AddDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Deliveries.AddAsync(StoredDelivery.Of(delivery), cancellationToken);
                var saved = await SaveAsync(database, cancellationToken);
                await PruneAsync(database.Deliveries, RunJournal.DeliveriesKept, cancellationToken);

                return saved;
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private static async Task<int> SaveAsync(TriggersDbContext database, CancellationToken cancellationToken)
    {
        var saved = await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return saved;
    }

    private static async Task PruneAsync(DbSet<StoredRun> rows, int kept, CancellationToken cancellationToken)
    {
        var newest = await rows.MaxAsync(row => (int?)row.Key, cancellationToken) ?? 0;
        await rows.Where(row => row.Key <= newest - kept).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task PruneAsync(DbSet<StoredDelivery> rows, int kept, CancellationToken cancellationToken)
    {
        var newest = await rows.MaxAsync(row => (int?)row.Key, cancellationToken) ?? 0;
        await rows.Where(row => row.Key <= newest - kept).ExecuteDeleteAsync(cancellationToken);
    }
}
