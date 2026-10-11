using Avala.Contests.Contracts;
using Avala.Contests.Running;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Contests.Storage;

internal sealed class StoredContest
{
    public Guid Id { get; init; }

    public long Started { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Contest { get; init; } = string.Empty;

    public static StoredContest Of(ContestRecord contest) => new()
    {
        Id = contest.Contest.Value,
        Started = contest.Started.UtcTicks,
        Status = contest.Status.ToString(),
        Contest = StoredJson.Write(contest),
    };

    public ContestRecord Read() => StoredJson.Read<ContestRecord>(Contest);
}

internal sealed class SqliteContestStore(AvalaPaths paths) : IContestStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<ContestsDbContext> owner = new(paths.Database("contests"), file => new ContestsDbContext(file));

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public Task<IReadOnlyList<ContestRecord>> ContestsAsync(CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<ContestRecord>>(
            async database => [.. (await database.Contests.AsNoTracking().OrderBy(row => row.Started).ToListAsync(cancellationToken)).Select(row => row.Read())],
            cancellationToken);

    public Task KeepAsync(ContestRecord contest, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database =>
            {
                var row = StoredContest.Of(contest);

                if (await database.Contests.FindAsync([row.Id], cancellationToken) is { } stored)
                {
                    database.Entry(stored).Property(kept => kept.Status).CurrentValue = row.Status;
                    database.Entry(stored).Property(kept => kept.Contest).CurrentValue = row.Contest;
                }
                else
                {
                    await database.Contests.AddAsync(row, cancellationToken);
                }

                var saved = await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();

                return saved;
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
