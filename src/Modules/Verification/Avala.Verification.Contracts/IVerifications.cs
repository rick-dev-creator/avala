using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Verification.Contracts;

public interface IVerifications
{
    IReadOnlyList<VerificationReport> OfJob(JobId job);
}

public interface IRepositoryChecks
{
    ValueTask<RepositoryChecks> OfRepositoryAsync(string repository, CancellationToken cancellationToken);
}

public interface IChecksDiscoveries
{
    Option<ChecksDiscovery> OfJob(JobId job);
}

public interface IChecksProposals
{
    ValueTask AcceptAsync(JobId job, CancellationToken cancellationToken);

    ValueTask DeclineAsync(JobId job, CancellationToken cancellationToken);
}
