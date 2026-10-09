using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;
using HoldAnnouncement = Avala.Jobs.Contracts.JobHeld;

namespace Avala.Jobs.Holding;

internal sealed class HoldJob(JobLedger ledger, IAgents agents, IEventBus bus)
{
    public async Task<Result<JobHold, JobRejection>> ExecuteAsync(Job job, HoldReason reason, CancellationToken cancellationToken) =>
        await job.Session.Match(
            session => job.Hold(reason).IsSuccess
                ? HeldAsync(job, session, reason, cancellationToken)
                : Task.FromResult(Result<JobHold, JobRejection>.Failure(JobRejection.NotRunning)),
            () => Task.FromResult(Result<JobHold, JobRejection>.Failure(JobRejection.NotRunning)));

    public async Task<Result<JobId, JobRejection>> DiscardAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Discard().IsFailure)
        {
            return JobRejection.NotDiscardable;
        }

        await ledger.RecordAsync(job, cancellationToken);

        return job.Id;
    }

    private async Task<Result<JobHold, JobRejection>> HeldAsync(Job job, SessionId session, HoldReason reason, CancellationToken cancellationToken)
    {
        await ledger.RecordAsync(job, cancellationToken);

        var hold = new JobHold(job.Id, session, reason, await HaltAsync(session, reason, cancellationToken));
        await bus.PublishAsync(new HoldAnnouncement(hold), cancellationToken);

        return hold;
    }

    private async Task<SessionHalt> HaltAsync(SessionId session, HoldReason reason, CancellationToken cancellationToken)
    {
        if (reason == HoldReason.SessionLost)
        {
            return await StopAsync(session, cancellationToken);
        }

        return await (await agents.InterruptAsync(session, cancellationToken)).Match(
            _ => Task.FromResult(SessionHalt.Interrupted),
            error => error switch
            {
                AgentError.NoTurnInProgress => Task.FromResult(SessionHalt.Idle),
                AgentError.SessionClosed => Task.FromResult(SessionHalt.AlreadyClosed),
                _ => StopAsync(session, cancellationToken),
            });
    }

    private async Task<SessionHalt> StopAsync(SessionId session, CancellationToken cancellationToken) =>
        (await agents.StopAsync(session, cancellationToken)).Match(_ => SessionHalt.Stopped, _ => SessionHalt.AlreadyClosed);
}
