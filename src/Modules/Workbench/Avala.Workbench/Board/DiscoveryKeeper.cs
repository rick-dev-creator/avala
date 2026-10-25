using Avala.Sdk.Events;
using Avala.Verification.Contracts;

namespace Avala.Workbench.Board;

internal sealed class DiscoveryKeeper(JobBoard board) : IHandle<ChecksDiscoveryChanged>
{
    public ValueTask HandleAsync(ChecksDiscoveryChanged integrationEvent, CancellationToken cancellationToken)
    {
        board.Publish(board.Discoveries.SetItem(integrationEvent.Discovery.Job, integrationEvent.Discovery));

        return ValueTask.CompletedTask;
    }
}
