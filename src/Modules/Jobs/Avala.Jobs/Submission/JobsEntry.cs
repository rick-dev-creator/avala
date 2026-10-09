using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Submission;

internal sealed class JobsEntry(SubmitJob submit, HoldJob hold, JobQueues queues) : IJobs
{
    public async ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        (await submit.ExecuteAsync(request.RepositoryPath, request.Instruction, request.AttemptsPerRound, cancellationToken))
            .MapError(Rejection);

    public async ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, (found, token) => hold.ExecuteAsync(found, reason, token), cancellationToken))
            .Match(held => held, () => Result<JobHold, JobRejection>.Failure(JobRejection.UnknownJob));

    private static JobRejection Rejection(JobError error) => error switch
    {
        JobError.EmptyRepository => JobRejection.EmptyRepository,
        JobError.EmptyInstruction => JobRejection.EmptyInstruction,
        JobError.InvalidAttemptBudget => JobRejection.InvalidAttemptBudget,
        _ => JobRejection.InvalidRequest,
    };
}
