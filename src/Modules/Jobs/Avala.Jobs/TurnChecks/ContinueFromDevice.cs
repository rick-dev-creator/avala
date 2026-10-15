using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.TurnChecks;

internal sealed class ContinueFromDevice(JobLedger ledger, JobQueues queues) : IHandle<RemoteMessageReceived>
{
    public async ValueTask HandleAsync(RemoteMessageReceived integrationEvent, CancellationToken cancellationToken)
    {
        if (!integrationEvent.BeganTurn || !Feedback.Create(integrationEvent.Text).TryGetValue(out var message, out _))
        {
            return;
        }

        await ledger.JobOfSessionAsync(integrationEvent.Session, cancellationToken).MatchAsync(
            job => queues.PostAsync(job, (found, token) => ContinueAsync(found, integrationEvent.Session, message, token), cancellationToken),
            () => Task.CompletedTask);
    }

    private async Task ContinueAsync(Job job, SessionId session, Feedback message, CancellationToken cancellationToken)
    {
        if (job.State == JobState.AwaitingReview && job.Session == Option<SessionId>.Some(session) && job.ContinueFromDevice(message).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
        }
    }
}
