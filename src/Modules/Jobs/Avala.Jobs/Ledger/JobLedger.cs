using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Ledger;

internal sealed class JobLedger(IJobStore store, IEventBus bus)
{
    public Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken) => store.FindAsync(id, cancellationToken);

    public Task<Option<Job>> FindBySessionAsync(SessionId session, CancellationToken cancellationToken) =>
        store.FindBySessionAsync(session, cancellationToken);

    public Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken) => store.ActiveAsync(cancellationToken);

    public async Task RecordAsync(Job job, CancellationToken cancellationToken)
    {
        await store.SaveAsync(job, cancellationToken);
        await bus.PublishAsync(new JobProgressed(job.Id, job.State.Status), cancellationToken);
    }

    public async Task RecordSessionAsync(Job job, SessionId session, CancellationToken cancellationToken)
    {
        await RecordAsync(job, cancellationToken);
        await bus.PublishAsync(new JobSessionStarted(job.Id, session), cancellationToken);
    }
}
