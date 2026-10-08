using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Application;

internal interface IWorkspaceStore
{
    Task SaveAsync(Workspace workspace, CancellationToken cancellationToken);

    Task<Option<Workspace>> FindAsync(WorkspaceId id, CancellationToken cancellationToken);

    Task RemoveAsync(WorkspaceId id, CancellationToken cancellationToken);
}
