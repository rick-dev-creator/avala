using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.TurnChecks;

internal sealed class CheckTurn(JobLedger ledger, JobQueues queues, EvaluateTurn evaluate, HoldJob hold)
    : IHandle<TurnFinished>, IHandle<SessionEnded>
{
    public async ValueTask HandleAsync(TurnFinished integrationEvent, CancellationToken cancellationToken) =>
        await ledger.JobOfSessionAsync(integrationEvent.Session, cancellationToken).MatchAsync(
            job => queues.PostAsync(
                job,
                (found, token) => evaluate.ExecuteAsync(found, integrationEvent.Session, integrationEvent.Outcome, token),
                cancellationToken),
            () => Task.CompletedTask);

    public async ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) =>
        await ledger.JobOfSessionAsync(integrationEvent.Session, cancellationToken).MatchAsync(
            job => queues.PostAsync(
                job,
                (found, token) => found.Session == Option<SessionId>.Some(integrationEvent.Session)
                    ? hold.ExecuteAsync(found, HoldReason.SessionLost, token)
                    : Task.CompletedTask,
                cancellationToken),
            () => Task.CompletedTask);
}
