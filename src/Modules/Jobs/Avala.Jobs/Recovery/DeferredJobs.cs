using System.Collections.Immutable;
using Avala.Jobs.Contracts;

namespace Avala.Jobs.Recovery;

internal sealed class DeferredJobs(IEnumerable<IRecoveryDeferral> deferrals)
{
    private ImmutableHashSet<JobId> deferred = [];

    public async Task<bool> DeferAsync(JobId job, CancellationToken cancellationToken)
    {
        foreach (var deferral in deferrals)
        {
            if (await deferral.DefersAsync(job, cancellationToken))
            {
                ImmutableInterlocked.Update(ref deferred, held => held.Add(job));

                return true;
            }
        }

        return false;
    }

    public bool Release(JobId job) => ImmutableInterlocked.Update(ref deferred, held => held.Remove(job));
}
