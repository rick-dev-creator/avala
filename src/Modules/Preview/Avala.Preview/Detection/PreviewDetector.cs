using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Running;
using Avala.Resources.Contracts;
using Avala.Sdk.Events;

namespace Avala.Preview.Detection;

internal sealed class PreviewDetector(IResources resources, PreviewBook book, RunBook runs, IEventBus bus)
    : IHandle<ResourcesSampled>, IHandle<JobProgressed>
{
    private static readonly JobStatus[] Ends = [JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    private ImmutableHashSet<JobId> ended = [];

    public ValueTask HandleAsync(ResourcesSampled integrationEvent, CancellationToken cancellationToken) =>
        ChangeAsync(Sightings.Of(integrationEvent.Sample, resources.Leases(), runs.OwnerOf, ended), cancellationToken);

    public ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (!Ends.Contains(integrationEvent.Status))
        {
            return ValueTask.CompletedTask;
        }

        ended = ended.Add(integrationEvent.Job);

        return ChangeAsync(book.Sighted.Remove(integrationEvent.Job), cancellationToken);
    }

    private async ValueTask ChangeAsync(ImmutableDictionary<JobId, Sighting> next, CancellationToken cancellationToken)
    {
        var before = book.Sighted;
        book.Keep(next);

        foreach (var job in before.Keys.Union(next.Keys))
        {
            var was = Targets(before, job);
            var now = Targets(next, job);

            foreach (var gone in was.Where(target => !now.Any(kept => kept.Address == target.Address)))
            {
                await bus.PublishAsync(new PreviewGone(gone), cancellationToken);
            }

            foreach (var available in now.Where(target => !was.Any(known => known.Address == target.Address)))
            {
                await bus.PublishAsync(new PreviewAvailable(available), cancellationToken);
            }
        }
    }

    private static ImmutableList<PreviewTarget> Targets(ImmutableDictionary<JobId, Sighting> sightings, JobId job) =>
        sightings.TryGetValue(job, out var sighting) ? sighting.Targets : [];
}
