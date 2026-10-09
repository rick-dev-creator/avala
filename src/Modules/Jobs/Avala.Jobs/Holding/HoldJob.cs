using System.Collections.Immutable;
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
    private ImmutableHashSet<TurnId> interrupted = [];

    public bool Settled(TurnId turn) => ImmutableInterlocked.Update(ref interrupted, turns => turns.Remove(turn));

    public async Task<Result<JobHold, JobRejection>> ExecuteAsync(Job job, HoldReason reason, CancellationToken cancellationToken) =>
        await job.Session.Match(
            session => job.Hold(reason).IsSuccess
                ? HeldAsync(job, session, reason, cancellationToken)
                : Task.FromResult(Result<JobHold, JobRejection>.Failure(JobRejection.NotRunning)),
            () => Task.FromResult(Result<JobHold, JobRejection>.Failure(JobRejection.NotRunning)));

    public async Task<Result<JobSteered, JobRejection>> SteerAsync(Job job, Feedback message, CancellationToken cancellationToken) =>
        await job.Session.Match(
            async session => job.State != JobState.Running
                ? JobRejection.NotRunning
                : (await agents.SteerAsync(session, message.Text, cancellationToken)).Match(
                    steered => Result<JobSteered, JobRejection>.Success(new JobSteered(job.Id, steered.Session, steered.Turn)),
                    error => Result<JobSteered, JobRejection>.Failure(SteeringRejection(error))),
            () => Task.FromResult(Result<JobSteered, JobRejection>.Failure(JobRejection.NotRunning)));

    private static JobRejection SteeringRejection(AgentError error) => error switch
    {
        AgentError.Unsupported => JobRejection.NotSteerable,
        AgentError.SessionClosed => JobRejection.AgentUnavailable,
        _ => JobRejection.NotRunning,
    };

    private async Task<Result<JobHold, JobRejection>> HeldAsync(Job job, SessionId session, HoldReason reason, CancellationToken cancellationToken)
    {
        await ledger.RecordAsync(job, cancellationToken);

        var hold = new JobHold(job.Id, session, reason, await HaltAsync(session, reason, cancellationToken));
        await bus.PublishAsync(new HoldAnnouncement(hold), cancellationToken);

        return hold;
    }

    private async Task<SessionHalt> HaltAsync(SessionId session, HoldReason reason, CancellationToken cancellationToken)
    {
        if (reason is HoldReason.SessionLost or HoldReason.Stopped)
        {
            return await StopAsync(session, cancellationToken);
        }

        return await (await agents.InterruptAsync(session, cancellationToken)).Match(
            turn =>
            {
                ImmutableInterlocked.Update(ref interrupted, turns => turns.Add(turn));

                return Task.FromResult(SessionHalt.Interrupted);
            },
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
