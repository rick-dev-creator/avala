using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Contracts;

public sealed record JobCheckpoint(int Attempt, int Number, IReadOnlyList<FileChange> Files);

public sealed record CheckpointRestored(JobId Job, int Attempt, IReadOnlyList<FileChange> Files);

public interface IJobCheckpoints
{
    ValueTask<IReadOnlyList<JobCheckpoint>> OfAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Result<CheckpointRestored, JobRejection>> RestoreAsync(JobId job, int attempt, CancellationToken cancellationToken);
}
