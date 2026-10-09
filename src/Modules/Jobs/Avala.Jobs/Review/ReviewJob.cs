using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;
using ApprovalAnnouncement = Avala.Jobs.Contracts.JobApproved;

namespace Avala.Jobs.Review;

internal sealed class ReviewJob(JobLedger ledger, JobLauncher launcher, Approvals approvals, IEventBus bus)
{
    public Task<Result<JobContinuation, JobRejection>> ContinueAsync(Job job, Feedback guidance, CancellationToken cancellationToken) =>
        launcher.ContinueAsync(job, guidance, cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> SendBackAsync(Job job, Feedback feedback, CancellationToken cancellationToken) =>
        launcher.SendBackAsync(job, feedback, cancellationToken);

    public async Task<Result<JobApproval, JobRejection>> ApproveAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State != JobState.AwaitingReview)
        {
            return JobRejection.NotAwaitingReview;
        }

        if (!(await approvals.DeliverAsync(job, cancellationToken)).TryGetValue(out var delivery, out var rejection))
        {
            return rejection;
        }

        _ = job.Approve();
        await ledger.RecordAsync(job, cancellationToken);

        var approval = new JobApproval(job.Id, delivery);
        await bus.PublishAsync(new ApprovalAnnouncement(approval), cancellationToken);

        return approval;
    }

    public async Task<Result<JobId, JobRejection>> DiscardAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Discard().IsFailure)
        {
            return JobRejection.NotDiscardable;
        }

        await ledger.RecordAsync(job, cancellationToken);

        return job.Id;
    }
}
