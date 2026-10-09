using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Steering;

internal enum MessageDelivery
{
    Sent,
    JoinedTheTurn,
    Queued,
}

internal sealed class JobSteering(IJobs jobs, QueuedMessages queue)
{
    public Task<Result<MessageDelivery, JobRejection>> SendAsync(JobId job, JobStatus shown, string message, CancellationToken cancellationToken) =>
        queue.DeliverAsync(job, shown, message, cancellationToken);

    public Option<string> Queued(JobId job) => queue.Find(job);

    public bool Withdraw(JobId job) => queue.Withdraw(job);

    public async Task<Result<JobId, JobRejection>> InterruptAsync(JobId job, CancellationToken cancellationToken) =>
        (await jobs.HoldAsync(job, HoldReason.Interrupted, cancellationToken)).Map(hold => hold.Job);

    public async Task<Result<JobId, JobRejection>> StopAsync(JobId job, CancellationToken cancellationToken) =>
        (await jobs.HoldAsync(job, HoldReason.Stopped, cancellationToken)).Map(hold => hold.Job);
}
