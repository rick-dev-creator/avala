using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Holding;

internal sealed class IdleSessions(JobLedger ledger, JobQueues queues, IAgents agents, TimeProvider clock)
    : IHandle<SessionOpened>, IHandle<SessionStopped>, IHandle<SessionEnded>, IHandle<JobProgressed>, IHandle<JobRemoteControlChosen>, IHandle<JobSessionStarted>, IHandle<TurnFinished>, IJobSessions, IDisposable
{
    public static readonly TimeSpan UndeclaredCache = TimeSpan.FromHours(1);

    private readonly Dictionary<JobId, ITimer> alarms = [];
    private readonly Dictionary<SessionId, JobId> jobs = [];
    private ImmutableDictionary<SessionId, TimeSpan> resumable = ImmutableDictionary<SessionId, TimeSpan>.Empty;

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Capabilities.Has<Resumable>())
        {
            ImmutableInterlocked.TryAdd(
                ref resumable,
                integrationEvent.Session,
                integrationEvent.Capabilities.Get<CachesPrompt>().Match(cache => cache.Lifetime, () => UndeclaredCache));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(SessionStopped integrationEvent, CancellationToken cancellationToken) => ForgetAsync(integrationEvent.Session);

    public ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) => ForgetAsync(integrationEvent.Session);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await ArmAsync(integrationEvent.Job, integrationEvent.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp, cancellationToken);

    public async ValueTask HandleAsync(JobRemoteControlChosen integrationEvent, CancellationToken cancellationToken) =>
        await ArmAsync(integrationEvent.Job, !integrationEvent.On, cancellationToken);

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        jobs[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(TurnFinished integrationEvent, CancellationToken cancellationToken)
    {
        if (jobs.TryGetValue(integrationEvent.Session, out var job))
        {
            await ArmAsync(job, waiting: true, cancellationToken);
        }
    }

    private ValueTask ForgetAsync(SessionId session)
    {
        ImmutableInterlocked.TryRemove(ref resumable, session, out _);

        return ValueTask.CompletedTask;
    }

    private async Task ArmAsync(JobId job, bool waiting, CancellationToken cancellationToken)
    {
        if (alarms.Remove(job, out var armed))
        {
            armed.Dispose();
        }

        if (!waiting)
        {
            return;
        }

        foreach (var (session, rounds, cache) in (await ledger.SnapshotAsync(job, cancellationToken)).Bind(Idle).Match<(SessionId, int, TimeSpan)[]>(found => [found], () => []))
        {
            alarms[job] = clock.CreateAlarm(() => _ = queues.PostAsync(job, (found, token) => RestIdleAsync(found, session, rounds, token), CancellationToken.None), cache);
        }
    }

    public async ValueTask<Result<JobId, JobRejection>> RestAsync(JobId job, CancellationToken cancellationToken) =>
        (await queues.RunAsync(
            job,
            async (found, token) => await found.Session.Match(session => RestAsync(found, session, found.Attempts.Count, token), () => Task.FromResult(false))
                ? Result<JobId, JobRejection>.Success(found.Id)
                : JobRejection.NotHeld,
            cancellationToken))
        .Match(rested => rested, () => JobRejection.UnknownJob);

    public void Dispose()
    {
        foreach (var alarm in alarms.Values)
        {
            alarm.Dispose();
        }

        alarms.Clear();
    }

    private Option<(SessionId Session, int Rounds, TimeSpan Cache)> Idle(Job job) =>
        job.State is not (JobState.AwaitingReview or JobState.NeedsHelp) ? Option<(SessionId, int, TimeSpan)>.None
        : job.Session.Bind(session => Volatile.Read(ref resumable).TryGetValue(session, out var cache)
            ? Option<(SessionId, int, TimeSpan)>.Some((session, job.Attempts.Count, cache))
            : Option<(SessionId, int, TimeSpan)>.None);

    private Task<bool> RestIdleAsync(Job job, SessionId session, int rounds, CancellationToken cancellationToken) =>
        job.Held == Option<HoldReason>.Some(HoldReason.LimitNearlyReached)
            ? Task.FromResult(false)
            : RestAsync(job, session, rounds, cancellationToken);

    private async Task<bool> RestAsync(Job job, SessionId session, int rounds, CancellationToken cancellationToken)
    {
        if (job.State is not (JobState.AwaitingReview or JobState.NeedsHelp)
            || job.RemoteControlled
            || job.Resume.IsNone
            || job.Session != Option<SessionId>.Some(session)
            || job.Attempts.Count != rounds
            || !Volatile.Read(ref resumable).ContainsKey(session)
            || !agents.IsOpen(session)
            || agents.IsWorking(session))
        {
            return false;
        }

        await ledger.AnnounceRestAsync(job.Id, session, cancellationToken);

        return (await agents.StopAsync(session, cancellationToken)).IsSuccess;
    }
}
