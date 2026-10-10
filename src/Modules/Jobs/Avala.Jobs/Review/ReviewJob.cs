using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Jobs.Recovery;
using Avala.Sdk;

namespace Avala.Jobs.Review;

internal sealed class ReviewJob(JobLedger ledger, JobLauncher launcher, Approvals approvals, ResumeJob resume)
{
    public Task<Result<JobContinuation, JobRejection>> ContinueAsync(Job job, Feedback guidance, CancellationToken cancellationToken) =>
        launcher.ContinueAsync(job, guidance, cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> ContinueOnAsync(
        Job job,
        ConnectionName connection,
        Feedback guidance,
        CancellationToken cancellationToken) =>
        launcher.ContinueOnAsync(job, connection, guidance, cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> SendBackAsync(Job job, Feedback feedback, CancellationToken cancellationToken) =>
        launcher.SendBackAsync(job, feedback, cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> HandOffAsync(Job job, JobHandoff handoff, CancellationToken cancellationToken) =>
        launcher.HandOffAsync(job, handoff, cancellationToken);

    public Task<Result<JobContinuation, JobRejection>> ResumeAsync(Job job, CancellationToken cancellationToken) =>
        resume.ExecuteAsync(job, cancellationToken);

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

        _ = job.Approve(ledger.Now);
        var approval = new JobApproval(job.Id, delivery);
        await ledger.RecordApprovalAsync(job, approval, cancellationToken);

        return approval;
    }

    public async Task<Result<JobId, JobRejection>> DiscardAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Discard(ledger.Now).IsFailure)
        {
            return JobRejection.NotDiscardable;
        }

        await ledger.RecordAsync(job, cancellationToken);

        return job.Id;
    }
}
