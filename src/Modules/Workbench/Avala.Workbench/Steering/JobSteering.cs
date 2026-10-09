using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Steering;

internal enum MessageDelivery
{
    Sent,
    JoinedTheTurn,
    Queued,
}

internal sealed class JobSteering(IJobs jobs, JobBoard board, QueuedMessages queue)
{
    public async Task<Result<MessageDelivery, JobRejection>> SendAsync(JobId job, string message, CancellationToken cancellationToken) =>
        await board.Find(job).Match(
            found => found.Status switch
            {
                JobStatus.NeedsHelp => SentAsync(jobs.ContinueAsync(job, message, cancellationToken)),
                JobStatus.AwaitingReview => SentAsync(jobs.SendBackAsync(job, message, cancellationToken)),
                JobStatus.Running when found.TakesMessagesMidTurn => SteeredAsync(job, message, cancellationToken),
                _ when found.Status.IsWorking => Task.FromResult(Queued(job, message)),
                _ => Task.FromResult(Result<MessageDelivery, JobRejection>.Failure(JobRejection.NotHeld)),
            },
            () => Task.FromResult(Result<MessageDelivery, JobRejection>.Failure(JobRejection.UnknownJob)));

    public Option<string> Queued(JobId job) => queue.Find(job);

    public bool Withdraw(JobId job) => queue.Withdraw(job);

    public async Task<Result<JobId, JobRejection>> InterruptAsync(JobId job, CancellationToken cancellationToken) =>
        (await jobs.HoldAsync(job, HoldReason.Interrupted, cancellationToken)).Map(hold => hold.Job);

    public async Task<Result<JobId, JobRejection>> StopAsync(JobId job, CancellationToken cancellationToken) =>
        (await jobs.HoldAsync(job, HoldReason.Stopped, cancellationToken)).Map(hold => hold.Job);

    private async Task<Result<MessageDelivery, JobRejection>> SteeredAsync(JobId job, string message, CancellationToken cancellationToken) =>
        (await jobs.SteerAsync(job, message, cancellationToken)).Match(
            _ => Result<MessageDelivery, JobRejection>.Success(MessageDelivery.JoinedTheTurn),
            rejection => rejection is JobRejection.NotSteerable or JobRejection.NotRunning
                ? Queued(job, message)
                : Result<MessageDelivery, JobRejection>.Failure(rejection));

    private Result<MessageDelivery, JobRejection> Queued(JobId job, string message)
    {
        queue.Queue(job, message);

        return MessageDelivery.Queued;
    }

    private static async Task<Result<MessageDelivery, JobRejection>> SentAsync(ValueTask<Result<JobContinuation, JobRejection>> continuation) =>
        (await continuation).Map(_ => MessageDelivery.Sent);
}
