using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Steering;

internal sealed class QueuedMessages(IJobs jobs) : IHandle<JobProgressed>, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private ImmutableDictionary<JobId, string> queued = ImmutableDictionary<JobId, string>.Empty;

    public Option<string> Find(JobId job) => Volatile.Read(ref queued).GetValueOrDefault(job).ToOption();

    public bool Withdraw(JobId job) => ImmutableInterlocked.TryRemove(ref queued, job, out _);

    public Task<Result<MessageDelivery, JobRejection>> DeliverAsync(JobId job, JobStatus shown, string message, CancellationToken cancellationToken) =>
        owner.RunAsync(token => DeliveredAsync(job, shown, message, token), cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(token => SentBackAsync(job, token), cancellationToken);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await owner.RunAsync(token => SettleAsync(integrationEvent, token), cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task<Result<MessageDelivery, JobRejection>> DeliveredAsync(JobId job, JobStatus shown, string message, CancellationToken cancellationToken)
    {
        Task<Result<MessageDelivery, JobRejection>> ContinuedAsync() =>
            AttemptAsync(jobs.ContinueAsync(job, message, cancellationToken), MessageDelivery.Sent, [JobRejection.NotHeld], () => Task.FromResult(Queued(job, message)));

        Task<Result<MessageDelivery, JobRejection>> SteeredAsync() =>
            AttemptAsync(jobs.SteerAsync(job, message, cancellationToken), MessageDelivery.JoinedTheTurn, [JobRejection.NotRunning, JobRejection.NotSteerable], ContinuedAsync);

        return shown switch
        {
            _ when shown.IsOver => JobRejection.NotHeld,
            JobStatus.AwaitingReview => await AttemptAsync(jobs.SendBackAsync(job, message, cancellationToken), MessageDelivery.Sent, [JobRejection.NotAwaitingReview], ContinuedAsync),
            JobStatus.Running => await SteeredAsync(),
            _ => await ContinuedAsync(),
        };
    }

    private static async Task<Result<MessageDelivery, JobRejection>> AttemptAsync<T>(
        ValueTask<Result<T, JobRejection>> attempt,
        MessageDelivery delivery,
        JobRejection[] elsewhere,
        Func<Task<Result<MessageDelivery, JobRejection>>> otherwise)
        where T : notnull =>
        await (await attempt).Match(
            _ => Task.FromResult(Result<MessageDelivery, JobRejection>.Success(delivery)),
            rejection => elsewhere.Contains(rejection) ? otherwise() : Task.FromResult(Result<MessageDelivery, JobRejection>.Failure(rejection)));

    private Result<MessageDelivery, JobRejection> Queued(JobId job, string message)
    {
        Queue(job, message);

        return MessageDelivery.Queued;
    }

    private async Task<Result<JobContinuation, JobRejection>> SentBackAsync(JobId job, CancellationToken cancellationToken)
    {
        if (!ImmutableInterlocked.TryRemove(ref queued, job, out var message))
        {
            return JobRejection.EmptyMessage;
        }

        var sent = await jobs.SendBackAsync(job, message, cancellationToken);

        if (!sent.IsSuccess)
        {
            Queue(job, message);
        }

        return sent;
    }

    private async Task SettleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var job = integrationEvent.Job;

        if (integrationEvent.Status.IsOver)
        {
            Withdraw(job);
            return;
        }

        if (integrationEvent.Status != JobStatus.NeedsHelp || !ImmutableInterlocked.TryRemove(ref queued, job, out var message))
        {
            return;
        }

        if (!(await jobs.ContinueAsync(job, message, cancellationToken)).IsSuccess)
        {
            Queue(job, message);
        }
    }

    private void Queue(JobId job, string message) =>
        ImmutableInterlocked.AddOrUpdate(ref queued, job, message, (_, earlier) => $"{earlier}\n\n{message}");
}
