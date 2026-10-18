using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Watching;

internal sealed class ResetRest(IJobSessions sessions) : IHandle<JobWaitsForReset>
{
    public async ValueTask HandleAsync(JobWaitsForReset integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Wait is { HasReset: true, AwaitsPerson: true })
        {
            _ = await sessions.RestAsync(integrationEvent.Wait.Job, cancellationToken);
        }
    }
}
