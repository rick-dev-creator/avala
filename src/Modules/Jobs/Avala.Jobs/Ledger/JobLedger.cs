using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Sdk.Events;
using ApprovalAnnouncement = Avala.Jobs.Contracts.JobApproved;

namespace Avala.Jobs.Ledger;

internal sealed class JobLedger(IJobStore store, IEventBus bus, IAgents agents)
{
    private ImmutableHashSet<JobId> recorded = [];

    public bool RecordedInThisRun(JobId job) => Volatile.Read(ref recorded).Contains(job);

    public Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken) => store.FindAsync(id, cancellationToken);

    public Task<Option<Job>> SnapshotAsync(JobId id, CancellationToken cancellationToken) => store.SnapshotAsync(id, cancellationToken);

    public Task<Option<JobId>> JobOfSessionAsync(SessionId session, CancellationToken cancellationToken) =>
        store.JobOfSessionAsync(session, cancellationToken);

    public Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken) => store.ActiveAsync(cancellationToken);

    public async Task RecordAsync(Job job, CancellationToken cancellationToken)
    {
        await SaveAsync(job, cancellationToken);

        if (job.State is JobState.Approved or JobState.Discarded or JobState.Failed)
        {
            await job.Session.Match<Task>(session => agents.StopAsync(session, cancellationToken).AsTask(), () => Task.CompletedTask);
        }

        await bus.PublishAsync(new JobProgressed(job.Id, job.State.Status), cancellationToken);
    }

    public async Task RecordApprovalAsync(Job job, JobApproval approval, CancellationToken cancellationToken)
    {
        await RecordAsync(job, cancellationToken);
        await bus.PublishAsync(new ApprovalAnnouncement(approval), cancellationToken);
    }

    public async Task RecordResumeAsync(Job job, SessionId session, CancellationToken cancellationToken)
    {
        await SaveAsync(job, cancellationToken);
        await bus.PublishAsync(new JobResumable(job.Id, session), cancellationToken);
    }

    public async Task RecordSessionAsync(Job job, SessionId session, CancellationToken cancellationToken)
    {
        await RecordAsync(job, cancellationToken);
        await bus.PublishAsync(new JobSessionStarted(job.Id, session) { Autonomy = job.Autonomy }, cancellationToken);
    }

    private Task SaveAsync(Job job, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref recorded, known => known.Add(job.Id));

        return store.SaveAsync(job, cancellationToken);
    }
}
