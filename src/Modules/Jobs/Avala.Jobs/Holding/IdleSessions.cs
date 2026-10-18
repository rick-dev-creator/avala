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
    : IHandle<SessionOpened>, IHandle<JobProgressed>, IJobSessions, IDisposable
{
    public static readonly TimeSpan UndeclaredCache = TimeSpan.FromHours(1);

    private readonly Dictionary<JobId, ITimer> alarms = [];
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

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var job = integrationEvent.Job;

        if (alarms.Remove(job, out var armed))
        {
            armed.Dispose();
        }

        if (integrationEvent.Status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp))
        {
            return;
        }

        foreach (var (session, rounds, cache) in (await ledger.SnapshotAsync(job, cancellationToken)).Bind(Idle).Match<(SessionId, int, TimeSpan)[]>(found => [found], () => []))
        {
            alarms[job] = clock.CreateAlarm(() => _ = queues.PostAsync(job, (found, token) => RestAsync(found, session, rounds, token), CancellationToken.None), cache);
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
        job.Session.Bind(session => Volatile.Read(ref resumable).TryGetValue(session, out var cache)
            ? Option<(SessionId, int, TimeSpan)>.Some((session, job.Attempts.Count, cache))
            : Option<(SessionId, int, TimeSpan)>.None);

    private async Task<bool> RestAsync(Job job, SessionId session, int rounds, CancellationToken cancellationToken)
    {
        if (job.State is not (JobState.AwaitingReview or JobState.NeedsHelp)
            || job.RemoteControlled
            || job.Resume.IsNone
            || job.Session != Option<SessionId>.Some(session)
            || job.Attempts.Count != rounds
            || !Volatile.Read(ref resumable).ContainsKey(session)
            || !agents.IsOpen(session))
        {
            return false;
        }

        return (await agents.StopAsync(session, cancellationToken)).IsSuccess;
    }
}
