using Avala.Forges.Contracts;
using Avala.Forges.Watching;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Forges.Storage;

internal sealed class ForgesDbContext(string database) : DbContext
{
    public DbSet<StoredWatch> Watches => Set<StoredWatch>();

    public DbSet<StoredWakeUp> WakeUps => Set<StoredWakeUp>();

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

internal sealed class SqliteWatchStore(AvalaPaths paths) : IWatchStore, IStartupTask, IAsyncDisposable
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

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
