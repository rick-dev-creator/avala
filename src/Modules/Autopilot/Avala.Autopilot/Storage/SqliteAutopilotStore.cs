using Avala.Autopilot.Contracts;
using Avala.Autopilot.Looping;
using Avala.Autopilot.Loops;
using Avala.Autopilot.Sourcing;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Autopilot.Storage;

internal sealed class SqliteAutopilotStore(AvalaPaths paths) : ITaskLedger, ILoopStore, IStartupTask, IAsyncDisposable
{
    public const int EndedLoopsKept = 100;

    private static readonly string Ended = LoopStatus.Ended.ToString();

    private readonly DatabaseOwner<AutopilotDbContext> owner = new(paths.Database("autopilot"), file => new AutopilotDbContext(file));

    public Task<IReadOnlyList<LoopRecord>> LoopsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<LoopRecord>>(
            async database => [.. (await database.Loops.AsNoTracking().OrderBy(row => row.Started).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepAsync(LoopRecord loop, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredLoop.Of(loop);

                if (await database.Loops.FindAsync([row.Loop], cancellationToken) is { } stored)
                {
                    stored.Status = row.Status;
                    stored.Fact = row.Fact;
                }
                else
                {
                    await database.Loops.AddAsync(row, cancellationToken);
                }

                var saved = await SaveAsync(database, cancellationToken);
                var surplus = await database.Loops.Where(stored => stored.Status == Ended).OrderByDescending(stored => stored.Started).Skip(EndedLoopsKept).ToListAsync(cancellationToken);
                database.Loops.RemoveRange(surplus);

                return saved + await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task<IReadOnlyList<LedgerEntry>> EntriesAsync(string repository, string source, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<LedgerEntry>>(
            async database =>
            [
                .. (await database.Tasks.AsNoTracking()
                        .Where(row => row.Repository == repository && row.Source == source)
                        .OrderBy(row => row.Key)
                        .ToListAsync(cancellationToken))
                    .Select(row => row.Entry()),
            ],
            cancellationToken);

    public Task ProposeAsync(SourcedTask task, DateTimeOffset at, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                await database.Tasks.AddAsync(StoredTask.Of(task, at), cancellationToken);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = await database.Tasks.SingleOrDefaultAsync(
                    stored => stored.Repository == task.Repository && stored.Source == task.Source && stored.TaskKey == task.Key,
                    cancellationToken);

                if (row is null)
                {
                    row = StoredTask.Of(task, mark.At);
                    await database.Tasks.AddAsync(row, cancellationToken);
                }

                row.Mark(mark);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private static async Task<int> SaveAsync(AutopilotDbContext database, CancellationToken cancellationToken)
    {
        var saved = await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return saved;
    }

    private Task<T> RunAsync<T>(Func<AutopilotDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
