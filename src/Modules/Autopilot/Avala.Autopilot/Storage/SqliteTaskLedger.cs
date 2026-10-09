using Avala.Autopilot.Contracts;
using Avala.Autopilot.Sourcing;
using Avala.Sdk;
using Microsoft.EntityFrameworkCore;

namespace Avala.Autopilot.Storage;

internal sealed class SqliteTaskLedger(AvalaPaths paths) : ITaskLedger, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private AutopilotDbContext? context;

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

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private static async Task<int> SaveAsync(AutopilotDbContext database, CancellationToken cancellationToken)
    {
        var saved = await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return saved;
    }

    private Task<T> RunAsync<T>(Func<AutopilotDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<AutopilotDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new AutopilotDbContext(paths.Database("autopilot"));
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        return context;
    }
}
