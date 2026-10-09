using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Jobs.Storage;

internal sealed class SqliteJobStore(AvalaPaths paths) : IJobStore, IStartupTask, IAsyncDisposable
{
    private static readonly JobState[] Active = [JobState.Preparing, JobState.Running, JobState.Checking];

    private readonly DatabaseOwner<JobsDbContext> owner = new(
        paths.Database("jobs"),
        file => new JobsDbContext(file) { ChangeTracker = { AutoDetectChangesEnabled = false } });

    public Task SaveAsync(Job job, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                if (database.Entry(job).State == EntityState.Detached)
                {
                    await database.Jobs.AddAsync(job, cancellationToken);
                }

                database.Entry(job).DetectChanges();

                foreach (var attempt in job.Attempts)
                {
                    database.Entry(attempt).DetectChanges();
                }

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken) =>
        RunAsync(async database => (await database.Jobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken)).ToOption(), cancellationToken);

    public Task<Option<JobId>> JobOfSessionAsync(SessionId session, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var wanted = Option<SessionId>.Some(session);
                var found = await database.Jobs.Where(job => job.Session == wanted).Select(job => job.Id).Take(1).ToListAsync(cancellationToken);

                return found.Count == 0 ? Option<JobId>.None : found[0];
            },
            cancellationToken);

    public Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<Job>>(
            async database => await database.Jobs.Where(job => Active.Contains(job.State)).ToListAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<Job>> SnapshotsAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<Job>>(async database => await database.Jobs.AsNoTracking().ToListAsync(cancellationToken), cancellationToken);

    public Task<Option<Job>> SnapshotAsync(JobId id, CancellationToken cancellationToken) =>
        RunAsync(
            async database => (await database.Jobs.AsNoTracking().FirstOrDefaultAsync(job => job.Id == id, cancellationToken)).ToOption(),
            cancellationToken);

    public Task RecordAsync(JobId id, ConnectionChoice choice, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var row = StoredChoice.Of(id, choice);
                await database.Choices.AddAsync(row, cancellationToken);
                var saved = await database.SaveChangesAsync(cancellationToken);
                database.Entry(row).State = EntityState.Detached;

                return saved;
            },
            cancellationToken);

    public Task<Option<ConnectionChoice>> ChoiceOfAsync(JobId id, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var wanted = id.Value;
                var found = await database.Choices.AsNoTracking().Where(row => row.Job == wanted).OrderByDescending(row => row.Key).Take(1).ToListAsync(cancellationToken);

                return found.Count == 0 ? Option<ConnectionChoice>.None : found[0].Read();
            },
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<T> RunAsync<T>(Func<JobsDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
