using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Supervision.Contracts;

public interface ISupervision
{
    ValueTask<SupervisionSettings> SettingsAsync(CancellationToken cancellationToken);

    ValueTask<Result<SupervisionSettings, SupervisionError>> ChangeSilenceAsync(TimeSpan silence, CancellationToken cancellationToken);

    IReadOnlyList<SupervisionIntervention> OfJob(JobId job);
}
