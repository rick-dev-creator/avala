using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Application;
using Avala.Jobs.Contracts;
using Avala.Jobs.Domain;
using Avala.Sdk;
using Microsoft.EntityFrameworkCore;

namespace Avala.Jobs.Infrastructure;

internal sealed class SqliteJobStore(AvalaPaths paths) : IJobStore, IAsyncDisposable
{
    private static readonly JobState[] Active = [JobState.Preparing, JobState.Running, JobState.Checking];

    private readonly SemaphoreSlim turn = new(1, 1);
    private JobsDbContext? context;

    public Task SaveAsync(Job job, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                if (database.Entry(job).State == EntityState.Detached)
                {
                    await database.Jobs.AddAsync(job, cancellationToken);
                }

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken) =>
        RunAsync(async database => (await database.Jobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken)).ToOption(), cancellationToken);

    public Task<Option<Job>> FindBySessionAsync(SessionId session, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                var wanted = Option<SessionId>.Some(session);

                return (await database.Jobs.FirstOrDefaultAsync(job => job.Session == wanted, cancellationToken)).ToOption();
            },
            cancellationToken);

    public Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<Job>>(
            async database => await database.Jobs.Where(job => Active.Contains(job.State)).ToListAsync(cancellationToken),
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (context is not null)
        {
            await context.DisposeAsync();
        }

        turn.Dispose();
    }

    private async Task<T> RunAsync<T>(Func<JobsDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        await turn.WaitAsync(cancellationToken);

        try
        {
            return await Task.Run(async () => await work(await OpenAsync(cancellationToken)), cancellationToken);
        }
        finally
        {
            turn.Release();
        }
    }

    private async Task<JobsDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new JobsDbContext(paths.Database("jobs"));
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        return context;
    }
}
