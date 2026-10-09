using System.Collections.Immutable;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;

namespace Avala.Delegation.Resuming;

internal sealed class DeferredParents(DelegationBook book) : IRecoveryDeferral
{
    private ImmutableHashSet<JobId> deferred = [];

    public IReadOnlySet<JobId> Waiting => Volatile.Read(ref deferred);

    public async ValueTask<bool> DefersAsync(JobId job, CancellationToken cancellationToken)
    {
        await book.RestoreAsync(cancellationToken);

        if (book.OwedTo(job).Count == 0)
        {
            return false;
        }

        ImmutableInterlocked.Update(ref deferred, waiting => waiting.Add(job));

        return true;
    }

    public bool Answered(JobId parent) =>
        Volatile.Read(ref deferred).Contains(parent)
        && book.OwedTo(parent).All(record => record.Report.IsSome)
        && ImmutableInterlocked.Update(ref deferred, waiting => waiting.Remove(parent));
}
