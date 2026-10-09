using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;

namespace Avala.Jobs.Ledger;

internal interface IJobStore
{
    Task SaveAsync(Job job, CancellationToken cancellationToken);

    Task<Option<Job>> FindAsync(JobId id, CancellationToken cancellationToken);

    Task<Option<JobId>> JobOfSessionAsync(SessionId session, CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> ActiveAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Job>> SnapshotsAsync(CancellationToken cancellationToken);

    Task<Option<Job>> SnapshotAsync(JobId id, CancellationToken cancellationToken);
}
