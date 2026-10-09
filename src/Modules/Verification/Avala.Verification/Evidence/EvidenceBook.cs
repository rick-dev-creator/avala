using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Verification.Evidence;

internal sealed class EvidenceBook(IEvidenceStore store) : IVerifications, IStartupTask
{
    private ImmutableList<VerificationReport> earlier = [];
    private ImmutableList<VerificationReport> reports = [];

    public async Task KeepAsync(VerificationReport report, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref reports, kept => kept.Add(report));
        await store.RecordAsync(report, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref earlier, [.. await store.EarlierRunsAsync(cancellationToken)]);

    public IReadOnlyList<VerificationReport> OfJob(JobId job) =>
        [.. Volatile.Read(ref earlier).Concat(Volatile.Read(ref reports)).Where(report => report.Job == job)];
}
