using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Jobs.Review;
using Avala.Sdk;

namespace Avala.Jobs.Submission;

internal sealed class JobsEntry(SubmitJob submit, JobControls controls, ReviewJob review, JobQueues queues) : IJobs, ISessionBoundJobs, IOpenDeliveries, IRemoteJobs, IJobMessages
{
    public async ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        await submit.ExecuteAsync(request, cancellationToken);

    public async ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => controls.Hold.ExecuteAsync(found, reason, token), cancellationToken);

    public ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        HoldRunningAsync(job, _ => true, reason, cancellationToken);

    public ValueTask<Result<JobHold, JobRejection>> HoldRunningOnAsync(JobId job, SessionId session, HoldReason reason, CancellationToken cancellationToken) =>
        HoldRunningAsync(job, found => found.Session == Option<SessionId>.Some(session), reason, cancellationToken);

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) =>
        ContinueAsync(job, new JobMessage(message), cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, message, review.ContinueAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(
        JobId job,
        ConnectionName connection,
        JobMessage message,
        CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, message, (found, guidance, token) => review.ContinueOnAsync(found, connection, guidance, token), cancellationToken);

    public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
        SteerAsync(job, _ => true, message, cancellationToken);

    public ValueTask<Result<JobSteered, JobRejection>> SteerOnAsync(JobId job, SessionId session, JobMessage message, CancellationToken cancellationToken) =>
        SteerAsync(job, found => found.Session == Option<SessionId>.Some(session), message, cancellationToken);

    public async ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        await InQueueAsync(job, review.DiscardAsync, cancellationToken);

    public async ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) =>
        await InQueueAsync(job, review.ApproveAsync, cancellationToken);

    public async ValueTask<Result<JobApproval, JobRejection>> ApproveThroughAsync(JobId job, string strategy, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => review.ApproveAsync(found, Option<string>.Some(strategy), token), cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ReopenAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, new JobMessage(feedback) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala }, review.ReopenAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        await SendBackAsync(job, new JobMessage(feedback), cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
        await WithFeedbackAsync(job, message, review.SendBackAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) =>
        await InQueueAsync(job, review.ResumeAsync, cancellationToken);

    public async ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => review.HandOffAsync(found, handoff, token), cancellationToken);

    public async ValueTask<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, ModelChoice model, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => controls.Model.ExecuteAsync(found, model, token), cancellationToken);

    public async ValueTask<Result<JobRemoteControlChosen, JobRejection>> ControlRemotelyAsync(JobId job, bool on, CancellationToken cancellationToken) =>
        await InQueueAsync(job, (found, token) => controls.Remote.ExecuteAsync(found, on, token), cancellationToken);

    private async Task<Result<JobContinuation, JobRejection>> WithFeedbackAsync(
        JobId job,
        JobMessage message,
        Func<Job, Feedback, CancellationToken, Task<Result<JobContinuation, JobRejection>>> round,
        CancellationToken cancellationToken) =>
        controls.Journal.Delivered(
            job,
            Feedback.Create(message.Text).Map(created => created.With(message.Attachments).By(message.Author).CausedBy(message.CausedBy).Carrying(message.Messages)).TryGetValue(out var feedback, out _)
                ? await InQueueAsync(job, (found, token) => round(found, feedback, token), cancellationToken)
                : JobRejection.EmptyMessage);

    private async ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, Func<Job, bool> on, HoldReason reason, CancellationToken cancellationToken) =>
        await InQueueAsync(
            job,
            (found, token) => found.State == JobState.Running && on(found)
                ? controls.Hold.ExecuteAsync(found, reason, token)
                : Task.FromResult(Result<JobHold, JobRejection>.Failure(JobRejection.NotRunning)),
            cancellationToken);

    private async ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, Func<Job, bool> on, JobMessage message, CancellationToken cancellationToken) =>
        controls.Journal.Delivered(
            job,
            Feedback.Create(message.Text).Map(created => created.With(message.Attachments).By(message.Author).CausedBy(message.CausedBy).Carrying(message.Messages)).TryGetValue(out var steering, out _)
                ? await InQueueAsync(
                    job,
                    (found, token) => on(found)
                        ? controls.Hold.SteerAsync(found, steering, token)
                        : Task.FromResult(Result<JobSteered, JobRejection>.Failure(JobRejection.NotRunning)),
                    cancellationToken)
                : JobRejection.EmptyMessage);

    private async Task<Result<T, JobRejection>> InQueueAsync<T>(
        JobId job,
        Func<Job, CancellationToken, Task<Result<T, JobRejection>>> work,
        CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, work, cancellationToken)).Match(done => done, () => Result<T, JobRejection>.Failure(JobRejection.UnknownJob));
}
