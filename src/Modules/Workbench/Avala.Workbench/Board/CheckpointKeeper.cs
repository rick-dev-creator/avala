using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Board;

internal sealed class CheckpointKeeper(IJobCheckpoints checkpoints, IJobCatalog catalog, JobBoard board) : IHandle<JobProgressed>, IHandle<StartupCompleted>
{
    private ImmutableDictionary<JobId, IReadOnlyList<JobCheckpoint>> kept = ImmutableDictionary<JobId, IReadOnlyList<JobCheckpoint>>.Empty;

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (Restorable(integrationEvent.Status))
        {
            await KeepAsync(integrationEvent.Job, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in (await catalog.ListAsync(cancellationToken)).Where(found => Restorable(found.Status)).Select(found => found.Job).ToList())
        {
            await KeepAsync(job, cancellationToken);
        }
    }

    private static bool Restorable(JobStatus status) => status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;

    private async Task KeepAsync(JobId job, CancellationToken cancellationToken)
    {
        var listed = await checkpoints.OfAsync(job, cancellationToken);
        kept = kept.SetItem(job, listed);
        board.Publish(kept);
    }
}
