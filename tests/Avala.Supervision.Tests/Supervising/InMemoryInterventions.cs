using System.Collections.Concurrent;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;

namespace Avala.Supervision.Tests.Supervising;

internal sealed class InMemoryInterventions : IInterventionStore
{
    private readonly ConcurrentQueue<SupervisionIntervention> recorded = new();

    public IReadOnlyList<SupervisionIntervention> Recorded => [.. recorded];

    public IReadOnlyList<SupervisionIntervention> Earlier { get; set; } = [];

    public Task RecordAsync(SupervisionIntervention intervention, CancellationToken cancellationToken)
    {
        recorded.Enqueue(intervention);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SupervisionIntervention>> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);
}
