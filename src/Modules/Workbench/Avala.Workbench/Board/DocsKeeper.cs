using System.Collections.Immutable;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Board;

internal sealed class DocsKeeper(IDocumentation documentation, IJobCatalog catalog, JobBoard board)
    : IHandle<FeatureChosen>, IHandle<VersionWriting>, IHandle<VersionWritten>, IHandle<VersionNotWritten>, IHandle<VersionWaiting>, IHandle<ChangeRequested>, IHandle<StartupCompleted>
{
    private ImmutableDictionary<JobId, JobDocumentation> kept = ImmutableDictionary<JobId, JobDocumentation>.Empty;

    public ValueTask HandleAsync(FeatureChosen integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Job);

    public ValueTask HandleAsync(VersionWriting integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Job);

    public ValueTask HandleAsync(VersionWritten integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Job);

    public ValueTask HandleAsync(VersionNotWritten integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Job);

    public ValueTask HandleAsync(VersionWaiting integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Job);

    public ValueTask HandleAsync(ChangeRequested integrationEvent, CancellationToken cancellationToken) => KeepAsync(integrationEvent.Job);

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in await catalog.ListAsync(cancellationToken))
        {
            await KeepAsync(job.Job);
        }
    }

    private ValueTask KeepAsync(JobId job)
    {
        kept = documentation.OfJob(job).Match(found => kept.SetItem(job, found), () => kept.Remove(job));
        board.Publish(kept);

        return ValueTask.CompletedTask;
    }
}
