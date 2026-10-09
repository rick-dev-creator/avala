using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Verification.Contracts;

namespace Avala.Verification.Evidence;

internal sealed class EvidenceBook : IVerifications
{
    private ImmutableDictionary<JobId, ImmutableList<VerificationReport>> jobs =
        ImmutableDictionary<JobId, ImmutableList<VerificationReport>>.Empty;

    public void Keep(VerificationReport report) =>
        ImmutableInterlocked.AddOrUpdate(ref jobs, report.Job, _ => [report], (_, reports) => reports.Add(report));

    public IReadOnlyList<VerificationReport> OfJob(JobId job) =>
        Volatile.Read(ref jobs).TryGetValue(job, out var reports) ? reports : [];
}
