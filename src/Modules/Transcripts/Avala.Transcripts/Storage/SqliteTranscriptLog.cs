using System.Data.Common;
using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Avala.Transcripts.Contracts;
using Avala.Transcripts.Keeping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Avala.Transcripts.Storage;

internal sealed partial class SqliteTranscriptLog : ITranscriptLog, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<TranscriptsDbContext> owner;
    private readonly Channel<PendingFact> pending = Channel.CreateUnbounded<PendingFact>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ILogger<SqliteTranscriptLog> logger;
    private readonly Task writer;

    public SqliteTranscriptLog(AvalaPaths paths, ILogger<SqliteTranscriptLog> logger)
    {
        owner = new DatabaseOwner<TranscriptsDbContext>(paths.Database("transcripts"), file => new TranscriptsDbContext(file));
        this.logger = logger;
        writer = Task.Run(WriteAsync);
    }

    public Guid Run { get; } = Guid.CreateVersion7();

    public void Keep(JobId job, DateTimeOffset at, ITranscriptFact fact) => _ = pending.Writer.TryWrite(new PendingFact(job, at, fact));

    public void Release(JobId job) => _ = pending.Writer.TryWrite(new PendingFact(job, default, new Released()));

    public Task<int> AttemptsAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async database => await database.Facts.AsNoTracking().Where(row => row.Job == job.Value).MaxAsync(row => (int?)row.Attempt, cancellationToken) ?? 0,
            cancellationToken);

    public Task<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync<IReadOnlyList<KeptFact>>(
            async database =>
            [
                .. (await database.Facts.AsNoTracking().Where(row => row.Job == job.Value && row.Run != Run).OrderBy(row => row.Key).ToListAsync(cancellationToken))
                    .SelectMany(row => row.Read().Match<KeptFact[]>(kept => [kept], () => [])),
            ],
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        pending.Writer.TryComplete();
        await writer;
        await owner.DisposeAsync();
    }

    private static List<PendingFact> Merged(List<PendingFact> batch)
    {
        var merged = new List<PendingFact>(batch.Count);
        var canvases = new Dictionary<(JobId, string), int>();

        foreach (var next in batch)
        {
            if (next.Fact is Released)
            {
                foreach (var slot in canvases.Keys.Where(slot => slot.Item1 == next.Job).ToList())
                {
                    canvases.Remove(slot);
                }
            }
            else if (next.Fact is CanvasDrawn drawn)
            {
                var slot = (next.Job, StoredFact.SlotOf(drawn.Snapshot.Canvas));

                if (canvases.TryGetValue(slot, out var position))
                {
                    merged[position] = merged[position] with { Fact = drawn };
                    continue;
                }

                canvases[slot] = merged.Count;
            }
            else if (merged.Count > 0 && Joined(merged[^1], next).Match(Replace, () => false))
            {
                continue;
            }

            merged.Add(next);
        }

        return merged;

        bool Replace(PendingFact joined)
        {
            merged[^1] = joined;

            return true;
        }
    }

    private static Option<PendingFact> Joined(PendingFact last, PendingFact next) =>
        (last.Fact, next.Fact) is (AgentActed { Event: ItemProgressed earlier }, AgentActed { Event: ItemProgressed later })
        && last.Job == next.Job
        && (earlier.Session, earlier.Turn, earlier.Item) == (later.Session, later.Turn, later.Item)
            ? last with { Fact = new AgentActed(earlier with { Text = earlier.Text + later.Text }) }
            : Option<PendingFact>.None;

    private async Task WriteAsync()
    {
        while (await pending.Reader.WaitToReadAsync())
        {
            var batch = new List<PendingFact>();

            while (pending.Reader.TryRead(out var next))
            {
                batch.Add(next);
            }

            try
            {
                _ = await owner.RunAsync(database => SaveAsync(database, Merged(batch)), CancellationToken.None);
            }
            catch (Exception failure) when (failure is DbException or DbUpdateException)
            {
                LogWriteFailed(batch.Count, failure);
            }
        }
    }

    private async Task<int> SaveAsync(TranscriptsDbContext database, List<PendingFact> facts)
    {
        var saved = 0;

        foreach (var fact in facts)
        {
            if (fact.Fact is Released)
            {
                saved += await database.SaveChangesAsync();
                database.ChangeTracker.Clear();
                saved += await database.Facts.Where(row => row.Job == fact.Job.Value && row.Kind != nameof(AttemptBegan)).ExecuteDeleteAsync();
                continue;
            }

            var row = StoredFact.Of(Run, fact);

            if (row.Slot.Length > 0 && await database.Facts.FirstOrDefaultAsync(stored => stored.Job == row.Job && stored.Slot == row.Slot) is { } stored)
            {
                database.Entry(stored).Property(fact => fact.Fact).CurrentValue = row.Fact;
            }
            else
            {
                _ = await database.Facts.AddAsync(row);
            }
        }

        saved += await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        return saved;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Keeping {Count} transcript facts failed")]
    private partial void LogWriteFailed(int count, Exception failure);
}
