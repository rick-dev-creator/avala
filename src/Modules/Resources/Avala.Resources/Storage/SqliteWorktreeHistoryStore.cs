using Avala.Resources.Contracts;
using Avala.Resources.History;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Resources.Storage;

internal sealed class SqliteWorktreeHistoryStore(AvalaPaths paths) : IWorktreeHistoryStore, IStartupTask, IAsyncDisposable
{
    public const int Kept = 500;

    private readonly DatabaseOwner<ResourcesDbContext> owner = new(paths.Database("resources"), file => new ResourcesDbContext(file));

    public StartupStage Stage => StartupStage.Open;

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    public Task RecordAsync(WorktreeEvent recorded, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.Events.AddAsync(StoredWorktreeEvent.Of(recorded), cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task SaveDayAsync(WorktreeDay day, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var key = StoredWorktreeDay.Key(day.Day);

                if (await database.Days.FirstOrDefaultAsync(row => row.Day == key, cancellationToken) is { } found)
                {
                    (found.Peak, found.Worktrees, found.Created, found.Freed, found.Recovered) = (day.Peak, day.Worktrees, day.Created, day.Freed, day.Recovered);
                }
                else
                {
                    await database.Days.AddAsync(new StoredWorktreeDay { Day = key, Peak = day.Peak, Worktrees = day.Worktrees, Created = day.Created, Freed = day.Freed, Recovered = day.Recovered }, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<Option<WorktreeDay>> DayAsync(DateOnly day, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var key = StoredWorktreeDay.Key(day);
                var found = await database.Days.AsNoTracking().FirstOrDefaultAsync(row => row.Day == key, cancellationToken);

                return found is null ? Option<WorktreeDay>.None : found.Of();
            },
            cancellationToken);

    public Task<bool> SeeAsync(string path, DateTimeOffset at, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                if (await database.Seen.AsNoTracking().AnyAsync(row => row.Path == path, cancellationToken))
                {
                    return false;
                }

                await database.Seen.AddAsync(new StoredSeenWorktree { Path = path, First = at.UtcTicks }, cancellationToken);
                _ = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return true;
            },
            cancellationToken);

    public Task<Option<DateTimeOffset>> FirstSeenAsync(string path, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var found = await database.Seen.AsNoTracking().FirstOrDefaultAsync(row => row.Path == path, cancellationToken);

                return found is null ? Option<DateTimeOffset>.None : new DateTimeOffset(found.First, TimeSpan.Zero);
            },
            cancellationToken);

    public Task<WorktreeReport> ReadAsync(DateOnly from, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var first = StoredWorktreeDay.Key(from);
                var days = (await database.Days.AsNoTracking().ToListAsync(cancellationToken)).Where(row => string.CompareOrdinal(row.Day, first) >= 0).OrderBy(row => row.Day, StringComparer.Ordinal).ToList();
                var events = await database.Events.AsNoTracking().OrderByDescending(row => row.At).Take(Kept).ToListAsync(cancellationToken);

                return new WorktreeReport([.. days.Select(row => row.Of())], [.. events.Select(row => row.Event())]);
            },
            cancellationToken);
}
