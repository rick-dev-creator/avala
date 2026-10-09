using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.Ledger;

internal sealed partial class JobQueues(JobLedger ledger, ILogger<JobQueues> logger) : IAsyncDisposable
{
    private readonly SerialExecutor routing = new();
    private readonly Dictionary<JobId, SerialExecutor> queues = [];

    public async Task<Option<T>> RunAsync<T>(JobId id, Func<Job, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
        where T : notnull =>
        await await routing.RunAsync(
            _ => Task.FromResult(QueueOf(id).RunAsync(token => LoadedAsync(id, work, token), cancellationToken)),
            cancellationToken);

    public Task PostAsync(JobId id, Func<Job, CancellationToken, Task> work, CancellationToken cancellationToken) =>
        routing.RunAsync(_ => EnqueueAsync(id, work, cancellationToken), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await routing.DisposeAsync();

        foreach (var queue in queues.Values)
        {
            await queue.DisposeAsync();
        }
    }

    private Task EnqueueAsync(JobId id, Func<Job, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        _ = QueueOf(id).RunAsync(token => LoggedAsync(id, work, token), cancellationToken);

        return Task.CompletedTask;
    }

    private SerialExecutor QueueOf(JobId id)
    {
        if (!queues.TryGetValue(id, out var queue))
        {
            queue = new SerialExecutor();
            queues.Add(id, queue);
        }

        return queue;
    }

    private async Task<Option<T>> LoadedAsync<T>(JobId id, Func<Job, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
        where T : notnull =>
        await ledger.FindAsync(id, cancellationToken).MatchAsync(
            async job => Option<T>.Some(await work(job, cancellationToken)),
            () => Task.FromResult(Option<T>.None));

    private async Task LoggedAsync(JobId id, Func<Job, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        try
        {
            await LoadedAsync(
                id,
                async (job, token) =>
                {
                    await work(job, token);

                    return true;
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogWorkFailed(id.Value, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Work on job {Job} failed")]
    private partial void LogWorkFailed(Guid job, Exception exception);
}
