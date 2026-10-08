using System.Collections.Concurrent;
using Avala.Workspaces.Application;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Infrastructure;

internal sealed class InMemoryWorkspaceStore : IWorkspaceStore
{
    private readonly ConcurrentDictionary<WorkspaceId, Workspace> workspaces = new();

    public int Count => workspaces.Count;

    public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        workspaces[workspace.Id] = workspace;

        return Task.CompletedTask;
    }

    public Task<Workspace?> FindAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        Task.FromResult(workspaces.GetValueOrDefault(id));

    public Task RemoveAsync(WorkspaceId id, CancellationToken cancellationToken)
    {
        workspaces.TryRemove(id, out _);

        return Task.CompletedTask;
    }
}
