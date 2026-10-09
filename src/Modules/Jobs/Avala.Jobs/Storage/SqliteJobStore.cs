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

    private readonly SerialExecutor serial = new();
    private JobsDbContext? context;

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

    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(_ => Task.FromResult(true), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private Task<T> RunAsync<T>(Func<JobsDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<JobsDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new JobsDbContext(paths.Database("jobs"));
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            await ModuleDatabase.MigrateAsync(context, cancellationToken);
        }

        return context;
    }
}
