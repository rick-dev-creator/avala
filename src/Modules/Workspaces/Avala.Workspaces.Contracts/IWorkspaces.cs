using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IWorkspaces
{
    ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken);

    ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken);

    ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken);

    ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken);

    ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken);
}

public sealed record WorktreeReconciliation(IReadOnlyList<string> Strays, IReadOnlyList<WorkspaceInfo> Missing);
