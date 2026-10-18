using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk.Events;

namespace Avala.Projects.Placing;

internal sealed class ArchivedAgents(IJobSessions sessions) : IHandle<ConversationsArchived>
{
    public async ValueTask HandleAsync(ConversationsArchived integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var job in integrationEvent.Jobs)
        {
            _ = await sessions.RestAsync(job, cancellationToken);
        }
    }
}
