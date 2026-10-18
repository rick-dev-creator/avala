namespace Avala.Workspaces.Contracts;

public interface IWorkspaceRemovals
{
    ValueTask RemovingAsync(WorkspaceInfo workspace, CancellationToken cancellationToken);
}
