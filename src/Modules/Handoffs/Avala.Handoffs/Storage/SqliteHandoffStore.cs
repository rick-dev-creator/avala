using Avala.Handoffs.Contracts;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Handoffs.Storage;

internal sealed class SqliteHandoffStore(AvalaPaths paths) : IHandoffStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<HandoffsDbContext> owner = new(paths.Database("handoffs"), file => new HandoffsDbContext(file));

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<IReadOnlyList<HandoffRecord>> HandoffsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<HandoffRecord>>(
            async database => [.. (await database.Handoffs.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task AddAsync(HandoffRecord handoff, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Handoffs.AddAsync(StoredHandoff.Of(handoff), cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<KeptWait>> WaitsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<KeptWait>>(
            async database => [.. (await database.Waits.AsNoTracking().OrderBy(row => row.Since).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepAsync(KeptWait wait, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredWait.Of(wait);

                if (await database.Waits.FindAsync([row.Job], cancellationToken) is { } stored)
                {
                    database.Entry(stored).Property(kept => kept.Pending).CurrentValue = row.Pending;
                    database.Entry(stored).Property(kept => kept.Since).CurrentValue = row.Since;
                }
                else
                {
                    await database.Waits.AddAsync(row, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task DropAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(database => database.Waits.Where(row => row.Job == job.Value).ExecuteDeleteAsync(cancellationToken), cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
