using Avala.Observability.Tracking;
using Avala.Observability.Usage;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Observability.Storage;

internal sealed class SqliteUsageStore(AvalaPaths paths) : IUsageStore, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private readonly HashSet<Guid> written = [];
    private ObservabilityDbContext? context;

    public Task KeepSessionAsync(SessionUsage session, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredSession.Of(session);
                written.Add(row.Session);

                if (await database.Sessions.FindAsync([row.Session], cancellationToken) is { } stored)
                {
                    database.Entry(stored).CurrentValues.SetValues(row);
                }
                else
                {
                    await database.Sessions.AddAsync(row, cancellationToken);
                }

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task RecordAsync(UsageFact fact, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                written.Add(fact.Session.Value);
                await database.Facts.AddAsync(StoredFact.Of(fact), cancellationToken);

                return await SaveAsync(database, cancellationToken);
            },
            cancellationToken);

    public Task<StoredUsage> EarlierRunsAsync(CancellationToken cancellationToken) =>
        RunAsync(
            async database => Usage(
                [.. (await database.Sessions.AsNoTracking().ToListAsync(cancellationToken)).Where(row => !written.Contains(row.Session))],
                [.. (await database.Facts.AsNoTracking().OrderBy(row => row.At).ToListAsync(cancellationToken)).Where(row => !written.Contains(row.Session))]),
            cancellationToken);

    public Task<StoredUsage> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var (start, end) = (from.UtcTicks, to.UtcTicks);
                var facts = await database.Facts.AsNoTracking().Where(row => row.At >= start && row.At < end).OrderBy(row => row.At).ToListAsync(cancellationToken);
                var sessions = facts.Select(row => row.Session).ToHashSet();

                return Usage([.. (await database.Sessions.AsNoTracking().ToListAsync(cancellationToken)).Where(row => sessions.Contains(row.Session))], facts);
            },
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(_ => Task.FromResult(true), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private static StoredUsage Usage(IReadOnlyList<StoredSession> sessions, IReadOnlyList<StoredFact> facts) =>
        new([.. sessions.Select(row => row.Usage())], [.. facts.Select(row => row.Fact())]);

    private static async Task<int> SaveAsync(ObservabilityDbContext database, CancellationToken cancellationToken)
    {
        var saved = await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        return saved;
    }

    private Task<T> RunAsync<T>(Func<ObservabilityDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<ObservabilityDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new ObservabilityDbContext(paths.Database("observability"));
            await ModuleDatabase.MigrateAsync(context, cancellationToken);
        }

        return context;
    }
}
