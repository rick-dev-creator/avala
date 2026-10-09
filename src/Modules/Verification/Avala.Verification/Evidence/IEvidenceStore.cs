using Avala.Verification.Contracts;

namespace Avala.Verification.Evidence;

internal interface IEvidenceStore
{
    Task RecordAsync(VerificationReport report, CancellationToken cancellationToken);

    Task<IReadOnlyList<VerificationReport>> EarlierRunsAsync(CancellationToken cancellationToken);
}
