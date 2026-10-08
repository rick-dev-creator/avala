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

    public Task SaveAsync(Job job, CancellationToken cancellationToken)
    {
        jobs[job.Id] = job;

        return Task.CompletedTask;
    }

    public Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.GetValueOrDefault(id).ToOption());

    public Task<Option<Job>> FindBySessionAsync(SessionId session, CancellationToken cancellationToken) =>
        Task.FromResult(jobs.Values.FirstOrDefault(job => job.Session == Option<SessionId>.Some(session)).ToOption());

    public Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Job>>([.. jobs.Values.Where(job => job.State is JobState.Preparing or JobState.Running or JobState.Checking)]);
}
