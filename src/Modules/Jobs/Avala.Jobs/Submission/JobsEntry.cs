using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Submission;

internal sealed class JobsEntry(SubmitJob submit, HoldJob hold, JobLauncher launcher, JobQueues queues) : IJobs
{
    public async ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        await submit.ExecuteAsync(request, cancellationToken);

    public async ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, (found, token) => hold.ExecuteAsync(found, reason, token), cancellationToken))
            .Match(held => held, () => Result<JobHold, JobRejection>.Failure(JobRejection.UnknownJob));

    public async ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken)
    {
        if (!Feedback.Create(message).TryGetValue(out var guidance, out _))
        {
            return JobRejection.EmptyMessage;
        }

        return (await queues.RunAsync(job, (found, token) => launcher.ContinueAsync(found, guidance, token), cancellationToken))
            .Match(continued => continued, () => Result<JobContinuation, JobRejection>.Failure(JobRejection.UnknownJob));
    }
}
