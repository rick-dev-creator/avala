using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal interface IInterventionStore
{
    Task RecordAsync(SupervisionIntervention intervention, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupervisionIntervention>> EarlierRunsAsync(CancellationToken cancellationToken);
}
