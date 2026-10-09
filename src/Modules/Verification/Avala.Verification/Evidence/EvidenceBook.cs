using System.Collections.Concurrent;
using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Verification.Contracts;

namespace Avala.Verification.Evidence;

internal sealed class EvidenceBook : IVerifications
{
    private readonly ConcurrentDictionary<JobId, ImmutableList<VerificationReport>> jobs = new();

    public void Keep(VerificationReport report) =>
        jobs.AddOrUpdate(report.Job, _ => [report], (_, reports) => reports.Add(report));

    public IReadOnlyList<VerificationReport> OfJob(JobId job) =>
        jobs.TryGetValue(job, out var reports) ? reports : [];
}
