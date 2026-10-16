using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public sealed record CheckpointChanges(int Number, string Label, string Commit, IReadOnlyList<FileChange> Files);

public interface IWorkspaceCheckpoints
{
    ValueTask<Result<IReadOnlyList<CheckpointChanges>, WorkspaceFailure>> ListAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<CheckpointChanges, WorkspaceFailure>> RestoreAsync(WorkspaceId workspace, int checkpoint, CancellationToken cancellationToken);
}
