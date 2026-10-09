using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.TurnChecks;

internal sealed class CheckTurn(JobLedger ledger, JobQueues queues, EvaluateTurn evaluate, HoldJob hold)
    : IHandle<TurnFinished>, IHandle<SessionEnded>, IHandle<SessionResumable>
{
    public async ValueTask HandleAsync(TurnFinished integrationEvent, CancellationToken cancellationToken) =>
        await OnJobOfAsync(
            integrationEvent.Session,
            (found, token) => evaluate.ExecuteAsync(found, integrationEvent.Session, integrationEvent.Outcome, token),
            cancellationToken);

    public async ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) =>
        await OnJobOfAsync(
            integrationEvent.Session,
            (found, token) => found.Session == Option<SessionId>.Some(integrationEvent.Session)
                ? hold.ExecuteAsync(found, HoldReason.SessionLost, token)
                : Task.CompletedTask,
            cancellationToken);

    public async ValueTask HandleAsync(SessionResumable integrationEvent, CancellationToken cancellationToken) =>
        await OnJobOfAsync(
            integrationEvent.Session,
            (found, token) => found.RecordResume(integrationEvent.Session, integrationEvent.Token).IsSuccess
                ? ledger.RecordResumeAsync(found, integrationEvent.Session, token)
                : Task.CompletedTask,
            cancellationToken);

    private Task OnJobOfAsync(SessionId session, Func<Job, CancellationToken, Task> work, CancellationToken cancellationToken) =>
        ledger.JobOfSessionAsync(session, cancellationToken).MatchAsync(
            job => queues.PostAsync(job, work, cancellationToken),
            () => Task.CompletedTask);
}
