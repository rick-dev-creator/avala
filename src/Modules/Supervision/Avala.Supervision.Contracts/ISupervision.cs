using Avala.Jobs.Contracts;

namespace Avala.Supervision.Contracts;

public interface ISupervision
{
    ValueTask<SupervisionSettings> SettingsAsync(CancellationToken cancellationToken);

    IReadOnlyList<SupervisionIntervention> OfJob(JobId job);
}
