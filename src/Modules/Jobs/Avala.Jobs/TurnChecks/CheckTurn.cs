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
    : IHandle<TurnFinished>, IHandle<SessionEnded>, IHandle<SessionResumable>, IHandle<RemoteMessageReceived>, IHandle<TurnWoken>
{
    public const string WokenNote = "The agent went on by itself, woken by work it had started in the background.";

    public async ValueTask HandleAsync(TurnFinished integrationEvent, CancellationToken cancellationToken) =>
        await OnJobOfAsync(
            integrationEvent.Session,
            (found, token) => hold.Settled(integrationEvent.Turn)
                ? Task.CompletedTask
                : evaluate.ExecuteAsync(found, integrationEvent, token),
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

    public async ValueTask HandleAsync(RemoteMessageReceived integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.BeganTurn && Feedback.Create(integrationEvent.Text).TryGetValue(out var message, out _))
        {
            await OnJobOfAsync(integrationEvent.Session, (found, token) => ContinueFromDeviceAsync(found, integrationEvent.Session, message, token), cancellationToken);
        }
    }

    public async ValueTask HandleAsync(TurnWoken integrationEvent, CancellationToken cancellationToken) =>
        await OnJobOfAsync(integrationEvent.Session, (found, token) => WakeAsync(found, integrationEvent.Session, token), cancellationToken);

    private async Task WakeAsync(Job job, SessionId session, CancellationToken cancellationToken)
    {
        if (job.State == JobState.AwaitingReview
            && job.Session == Option<SessionId>.Some(session)
            && Feedback.Create(WokenNote).TryGetValue(out var note, out _)
            && job.Wake(note).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
        }
    }

    private async Task ContinueFromDeviceAsync(Job job, SessionId session, Feedback message, CancellationToken cancellationToken)
    {
        if (job.State == JobState.AwaitingReview && job.Session == Option<SessionId>.Some(session) && job.ContinueFromDevice(message).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
        }
    }

    private Task OnJobOfAsync(SessionId session, Func<Job, CancellationToken, Task> work, CancellationToken cancellationToken) =>
        ledger.JobOfSessionAsync(session, cancellationToken).MatchAsync(
            job => queues.PostAsync(job, work, cancellationToken),
            () => Task.CompletedTask);
}
