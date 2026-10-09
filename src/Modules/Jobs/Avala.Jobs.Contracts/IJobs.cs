using Avala.Sdk;

namespace Avala.Jobs.Contracts;

public interface IJobs
{
    ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken);

    ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken);
}

public sealed record JobRequest(string RepositoryPath, string Instruction, int AttemptsPerRound = 3);

public enum JobRejection
{
    EmptyRepository,
    EmptyInstruction,
    InvalidAttemptBudget,
    InvalidRequest,
    UnknownJob,
    NotRunning,
}
