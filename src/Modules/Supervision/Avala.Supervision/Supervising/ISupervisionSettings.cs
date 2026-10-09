using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal interface ISupervisionSettings
{
    ValueTask<SupervisionSettings> LoadAsync(CancellationToken cancellationToken);
}
