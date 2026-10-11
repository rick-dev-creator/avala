using Avala.Forges.Contracts;
using Avala.Forges.Polling;
using Avala.Forges.Watching;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Forges.Storage;

internal sealed class ForgesDbContext(string database) : DbContext
{
    public DbSet<StoredWatch> Watches => Set<StoredWatch>();

    public DbSet<StoredWakeUp> WakeUps => Set<StoredWakeUp>();

    public DbSet<StoredCursor> Cursors => Set<StoredCursor>();

    public DbSet<StoredEventEntry> EventEntries => Set<StoredEventEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredWatch>(watch =>
        {
            watch.ToTable("Watches");
            watch.HasKey(row => row.Job);
        });
        modelBuilder.Entity<StoredWakeUp>(wakeUp =>
        {
            wakeUp.ToTable("WakeUps");
            wakeUp.HasKey(row => row.Key);
            wakeUp.Property(row => row.Key).ValueGeneratedOnAdd();
            wakeUp.HasIndex(row => row.Job);
        });
        modelBuilder.Entity<StoredCursor>(cursor =>
        {
            cursor.ToTable("EventCursors");
            cursor.HasKey(row => row.Trigger);
        });
        modelBuilder.Entity<StoredEventEntry>(entry =>
        {
            entry.ToTable("EventEntries");
            entry.HasKey(row => row.Key);
            entry.Property(row => row.Key).ValueGeneratedOnAdd();
        });
    }
}

internal sealed class StoredWatch
{
    public Guid Job { get; init; }

    public string Watch { get; set; } = string.Empty;

    public static StoredWatch Of(KeptWatch kept) => new() { Job = kept.State.Job.Value, Watch = StoredJson.Write(kept) };

    public KeptWatch Read() => StoredJson.Read<KeptWatch>(Watch);
}

internal sealed class StoredWakeUp
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public string WakeUp { get; init; } = string.Empty;

    public static StoredWakeUp Of(WakeUpRecord wakeUp) => new() { Job = wakeUp.Job.Value, WakeUp = StoredJson.Write(wakeUp) };

    public WakeUpRecord Read() => StoredJson.Read<WakeUpRecord>(WakeUp);
}

internal sealed class StoredCursor
{
    public string Trigger { get; init; } = string.Empty;

    public string Cursor { get; set; } = string.Empty;

    public static StoredCursor Of(EventCursor cursor) => new() { Trigger = cursor.Trigger, Cursor = StoredJson.Write(cursor) };

    public EventCursor Read() => StoredJson.Read<EventCursor>(Cursor);
}

internal sealed class StoredEventEntry
{
    public int Key { get; init; }

    public string Entry { get; init; } = string.Empty;

    public static StoredEventEntry Of(ForgeEventEntry entry) => new() { Entry = StoredJson.Write(entry) };

    public ForgeEventEntry Read() => StoredJson.Read<ForgeEventEntry>(Entry);
}

internal sealed class SqliteWatchStore(AvalaPaths paths) : IWatchStore, IEventStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<ForgesDbContext> owner = new(paths.Database("forges"), file => new ForgesDbContext(file));

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<IReadOnlyList<KeptWatch>> WatchesAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<KeptWatch>>(
            async database => [.. (await database.Watches.AsNoTracking().ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepAsync(KeptWatch watch, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredWatch.Of(watch);

                if (await database.Watches.FindAsync([row.Job], cancellationToken) is { } stored)
                {
                    stored.Watch = row.Watch;
                }
                else
                {
                    await database.Watches.AddAsync(row, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<WakeUpRecord>> WakeUpsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<WakeUpRecord>>(
            async database => [.. (await database.WakeUps.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task AddAsync(WakeUpRecord wakeUp, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.WakeUps.AddAsync(StoredWakeUp.Of(wakeUp), cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<EventCursor>> CursorsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<EventCursor>>(
            async database => [.. (await database.Cursors.AsNoTracking().ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepCursorAsync(EventCursor cursor, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredCursor.Of(cursor);

                if (await database.Cursors.FindAsync([row.Trigger], cancellationToken) is { } stored)
                {
                    stored.Cursor = row.Cursor;
                }
                else
                {
                    await database.Cursors.AddAsync(row, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public Task<IReadOnlyList<ForgeEventEntry>> EntriesAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<ForgeEventEntry>>(
            async database => [.. (await database.EventEntries.AsNoTracking().OrderBy(row => row.Key).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task AddEntryAsync(ForgeEventEntry entry, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                await database.EventEntries.AddAsync(StoredEventEntry.Of(entry), cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();
                var newest = await database.EventEntries.MaxAsync(row => (int?)row.Key, cancellationToken) ?? 0;
                await database.EventEntries.Where(row => row.Key <= newest - EventBook.EntriesKept).ExecuteDeleteAsync(cancellationToken);

                return saved;
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
