using System.Collections.Immutable;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Resuming;

internal sealed class ParentResumption(DelegationBook book, IJobs jobs) : IRecoveryDeferral, IHandle<ChildReported>, IHandle<StartupCompleted>
{
    private ImmutableHashSet<JobId> deferred = [];

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

    public async ValueTask HandleAsync(ChildReported integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var parent in integrationEvent.Delegation.Parent.Match<JobId[]>(found => [found], () => []))
        {
            await ResumeWhenAnsweredAsync(parent, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var parent in Volatile.Read(ref deferred))
        {
            await ResumeWhenAnsweredAsync(parent, cancellationToken);
        }
    }

    private async Task ResumeWhenAnsweredAsync(JobId parent, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref deferred).Contains(parent)
            && book.OwedTo(parent).All(record => record.Report.IsSome)
            && ImmutableInterlocked.Update(ref deferred, waiting => waiting.Remove(parent)))
        {
            _ = await jobs.ResumeAsync(parent, cancellationToken);
        }
    }
}
