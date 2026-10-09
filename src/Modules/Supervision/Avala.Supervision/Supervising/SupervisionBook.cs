using System.Collections.Concurrent;
using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Supervising;

internal sealed class SupervisionBook(ISupervisionSettings settings) : ISupervision
{
    private readonly ConcurrentDictionary<JobId, JobWatch> watches = new();
    private readonly ConcurrentDictionary<SessionId, JobId> jobs = new();
    private ImmutableList<SupervisionIntervention> interventions = [];

    public JobWatch Watch(JobId job) => watches.GetValueOrDefault(job) ?? new JobWatch(job);

    public void Keep(JobWatch watch) => watches[watch.Job] = watch;

    public void Tie(SessionId session, JobId job) => jobs[session] = job;

    public Option<JobId> JobOf(SessionId session) => jobs.TryGetValue(session, out var job) ? job : Option<JobId>.None;

    public void Record(SupervisionIntervention intervention) =>
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));

    public ValueTask<SupervisionSettings> SettingsAsync(CancellationToken cancellationToken) => settings.LoadAsync(cancellationToken);

    public IReadOnlyList<SupervisionIntervention> OfJob(JobId job) => [.. interventions.Where(intervention => intervention.Hold.Job == job)];
}
