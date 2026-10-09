using System.Collections.Concurrent;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<JobId, Job> jobs = new();

    public IReadOnlyList<Job> Jobs => [.. jobs.Values];

    public Action<Job> Saving { get; set; } = _ => { };

    public Task SaveAsync(Job job, CancellationToken cancellationToken)
    {
        Saving(job);
        jobs[job.Id] = job;

        return Task.CompletedTask;
    }

    public Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.GetValueOrDefault(id).ToOption());

    public Task<Option<JobId>> JobOfSessionAsync(SessionId session, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.Values.FirstOrDefault(job => job.Session == Option<SessionId>.Some(session)).ToOption().Map(job => job.Id));

    public Task<IReadOnlyList<Job>> SnapshotsAsync(CancellationToken cancellationToken) => Task.FromResult(Jobs);

    public Task<Option<Job>> SnapshotAsync(JobId id, CancellationToken cancellationToken) => FindAsync(id, cancellationToken);

    public ConcurrentDictionary<JobId, ConnectionChoice> Choices { get; } = new();

    public Task RecordAsync(JobId id, ConnectionChoice choice, CancellationToken cancellationToken)
    {
        Choices[id] = choice;

        return Task.CompletedTask;
    }

    public Task<Option<ConnectionChoice>> ChoiceOfAsync(JobId id, CancellationToken cancellationToken) =>
        Task.FromResult(Choices.GetValueOrDefault(id).ToOption());

    public Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Job>>([.. jobs.Values.Where(job => job.State is JobState.Preparing or JobState.Running or JobState.Checking)]);
}
