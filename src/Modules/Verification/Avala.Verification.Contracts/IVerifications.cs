using Avala.Jobs.Contracts;

namespace Avala.Verification.Contracts;

public interface IVerifications
{
    IReadOnlyList<VerificationReport> OfJob(JobId job);
}

public interface IRepositoryChecks
{
    ValueTask<RepositoryChecks> OfRepositoryAsync(string repository, CancellationToken cancellationToken);
}
