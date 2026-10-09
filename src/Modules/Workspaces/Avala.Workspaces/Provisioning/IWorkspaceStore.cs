using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal interface IWorkspaceStore
{
    Task SaveAsync(Workspace workspace, CancellationToken cancellationToken);

    Task<Option<Workspace>> FindAsync(WorkspaceId id, CancellationToken cancellationToken);

    Task<Option<Workspace>> FindAtAsync(string path, CancellationToken cancellationToken);

    Task RemoveAsync(WorkspaceId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Workspace>> AllAsync(CancellationToken cancellationToken);
}
