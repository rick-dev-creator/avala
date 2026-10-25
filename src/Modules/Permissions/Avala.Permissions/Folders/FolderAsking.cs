using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Permissions.Folders;

internal sealed class FolderAsking(GovernanceBook book, TimeProvider clock, IEventBus bus)
{
    public async Task AskAsync(GovernedSession governed, ToolCalled called, FolderRequest request, CancellationToken cancellationToken)
    {
        var decision = new PolicyDecision(
            called.Session,
            called.Turn,
            called.Item,
            governed.Job,
            ItemKind.Other,
            request.Folder,
            PolicyAnswer.Ask,
            Option<PolicyRule>.None,
            DecisionDelivery.LeftToHuman,
            clock.GetUtcNow())
        {
            Autonomy = governed.Policy.Autonomy,
            Folder = request,
        };

        await book.DecidedAsync(decision, cancellationToken);
        await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
    }
}
