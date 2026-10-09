using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Approving;

internal sealed class RunEvidenceQuery(EvidenceGatherer evidence) : IRunEvidence
{
    public async ValueTask<Option<RunEvidence>> OfJobAsync(JobId job, CancellationToken cancellationToken) =>
        (await evidence.FindAsync(job, cancellationToken)).Map(found => found.Run(job));
}
