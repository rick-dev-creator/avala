using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Application;

internal interface IWorkspaceStore
{
    Task SaveAsync(Workspace workspace, CancellationToken cancellationToken);

    Task<Workspace?> FindAsync(WorkspaceId id, CancellationToken cancellationToken);

    Task RemoveAsync(WorkspaceId id, CancellationToken cancellationToken);
}
