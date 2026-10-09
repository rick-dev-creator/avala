using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal sealed class SupervisionBook(ISupervisionSettings settings, IInterventionStore store) : ISupervision, IStartupTask
{
    private ImmutableList<SupervisionIntervention> earlier = [];
    private ImmutableList<SupervisionIntervention> interventions = [];

    public async Task RecordAsync(SupervisionIntervention intervention, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));
        await store.RecordAsync(intervention, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref earlier, [.. await store.EarlierRunsAsync(cancellationToken)]);

    public ValueTask<SupervisionSettings> SettingsAsync(CancellationToken cancellationToken) => settings.LoadAsync(cancellationToken);

    public ValueTask<Result<SupervisionSettings, SupervisionError>> ChangeSilenceAsync(TimeSpan silence, CancellationToken cancellationToken) =>
        settings.ChangeSilenceAsync(silence, cancellationToken);

    public IReadOnlyList<SupervisionIntervention> OfJob(JobId job) =>
        [.. Volatile.Read(ref earlier).Concat(Volatile.Read(ref interventions)).Where(intervention => intervention.Hold.Job == job)];
}
