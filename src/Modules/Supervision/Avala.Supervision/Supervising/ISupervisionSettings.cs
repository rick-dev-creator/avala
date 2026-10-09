using Avala.Sdk;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal interface ISupervisionSettings
{
    ValueTask<SupervisionSettings> LoadAsync(CancellationToken cancellationToken);

    ValueTask<Result<SupervisionSettings, SupervisionError>> ChangeSilenceAsync(TimeSpan silence, CancellationToken cancellationToken);
}
