using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal sealed class SupervisionBook(ISupervisionSettings settings) : ISupervision
{
    private ImmutableList<SupervisionIntervention> interventions = [];

    public void Record(SupervisionIntervention intervention) =>
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));

    public ValueTask<SupervisionSettings> SettingsAsync(CancellationToken cancellationToken) => settings.LoadAsync(cancellationToken);

    public IReadOnlyList<SupervisionIntervention> OfJob(JobId job) =>
        [.. Volatile.Read(ref interventions).Where(intervention => intervention.Hold.Job == job)];
}
