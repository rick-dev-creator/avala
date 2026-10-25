using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IWorkspaceShelf
{
    ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> ShelveAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> RebuildAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<bool, WorkspaceFailure>> DeliveredAsync(WorkspaceId workspace, CancellationToken cancellationToken);
}
