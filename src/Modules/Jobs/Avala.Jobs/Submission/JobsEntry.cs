using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Jobs.Review;
using Avala.Sdk;

namespace Avala.Jobs.Submission;

internal sealed class JobsEntry(SubmitJob submit, HoldJob hold, ReviewJob review, JobQueues queues) : IJobs
{
    public async ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        await submit.ExecuteAsync(request, cancellationToken);

    public async ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => hold.ExecuteAsync(found, reason, token), cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, message, review.ContinueAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(
        JobId job,
        ConnectionName connection,
        string message,
        CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, message, (found, guidance, token) => review.ContinueOnAsync(found, connection, guidance, token), cancellationToken);

    public async ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) =>
        Feedback.Create(message).TryGetValue(out var steering, out _)
            ? await InQueueAsync(job, (found, token) => hold.SteerAsync(found, steering, token), cancellationToken)
            : JobRejection.EmptyMessage;

    public async ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        await InQueueAsync(job, review.DiscardAsync, cancellationToken);

    public async ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) =>
        await InQueueAsync(job, review.ApproveAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, feedback, review.SendBackAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) =>
        await InQueueAsync(job, review.ResumeAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => review.HandOffAsync(found, handoff, token), cancellationToken);

    private async Task<Result<JobContinuation, JobRejection>> WithFeedbackAsync(
        JobId job,
        string message,
        Func<Job, Feedback, CancellationToken, Task<Result<JobContinuation, JobRejection>>> round,
        CancellationToken cancellationToken) =>
        Feedback.Create(message).TryGetValue(out var feedback, out _)
            ? await InQueueAsync(job, (found, token) => round(found, feedback, token), cancellationToken)
            : JobRejection.EmptyMessage;

    private async Task<Result<T, JobRejection>> InQueueAsync<T>(
        JobId job,
        Func<Job, CancellationToken, Task<Result<T, JobRejection>>> work,
        CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, work, cancellationToken)).Match(done => done, () => Result<T, JobRejection>.Failure(JobRejection.UnknownJob));
}
