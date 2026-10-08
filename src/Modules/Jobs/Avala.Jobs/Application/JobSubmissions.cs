using Avala.Jobs.Contracts;
using Avala.Jobs.Domain;
using Avala.Sdk;

namespace Avala.Jobs.Application;

internal sealed class JobSubmissions(SubmitJob submit) : IJobs
{
    public async ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        (await submit.ExecuteAsync(request.RepositoryPath, request.Instruction, request.AttemptsPerRound, cancellationToken))
            .MapError(Rejection);

    private static JobRejection Rejection(JobError error) => error switch
    {
        JobError.EmptyRepository => JobRejection.EmptyRepository,
        JobError.EmptyInstruction => JobRejection.EmptyInstruction,
        JobError.InvalidAttemptBudget => JobRejection.InvalidAttemptBudget,
        _ => JobRejection.InvalidRequest,
    };
}
