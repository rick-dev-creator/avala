using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Steering;

internal sealed class JobSteering(IJobs jobs, JobBoard board)
{
    public async Task<Result<JobId, JobRejection>> SendAsync(JobId job, string message, CancellationToken cancellationToken) =>
        await board.Find(job).Match(
            found => found.Status switch
            {
                JobStatus.NeedsHelp => ContinuedAsync(jobs.ContinueAsync(job, message, cancellationToken)),
                JobStatus.AwaitingReview => ContinuedAsync(jobs.SendBackAsync(job, message, cancellationToken)),
                _ => Task.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.NotHeld)),
            },
            () => Task.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.UnknownJob)));

    public async Task<Result<JobId, JobRejection>> InterruptAsync(JobId job, CancellationToken cancellationToken) =>
        (await jobs.HoldAsync(job, HoldReason.Interrupted, cancellationToken)).Map(hold => hold.Job);

    public async Task<Result<JobId, JobRejection>> StopAsync(JobId job, CancellationToken cancellationToken) =>
        (await jobs.HoldAsync(job, HoldReason.Stopped, cancellationToken)).Map(hold => hold.Job);

    private static async Task<Result<JobId, JobRejection>> ContinuedAsync(ValueTask<Result<JobContinuation, JobRejection>> continuation) =>
        (await continuation).Map(continued => continued.Job);
}
