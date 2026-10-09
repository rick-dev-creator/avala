using Avala.Verification.Contracts;
using Avala.Verification.Evidence;

namespace Avala.Verification.Tests.Verifying;

internal sealed class InMemoryEvidence : IEvidenceStore
{
    public IReadOnlyList<VerificationReport> Earlier { get; set; } = [];

    public List<VerificationReport> Recorded { get; } = [];

    public Task RecordAsync(VerificationReport report, CancellationToken cancellationToken)
    {
        Recorded.Add(report);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VerificationReport>> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);
}
