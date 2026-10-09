using Avala.Sdk;
using Avala.Storage;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Avala.Workspaces.Storage;

internal sealed class SqliteWorkspaceStore(AvalaPaths paths) : IWorkspaceStore, IStartupTask, IAsyncDisposable
{
    private readonly DatabaseOwner<WorkspacesDbContext> owner = new(
        paths.Database("workspaces"),
        file => new WorkspacesDbContext(file) { ChangeTracker = { AutoDetectChangesEnabled = false } });

    public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                if (database.Entry(workspace).State == EntityState.Detached)
                {
                    await database.Workspaces.AddAsync(workspace, cancellationToken);
                }

                database.Entry(workspace).DetectChanges();

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task<Option<Workspace>> FindAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        RunAsync(
            async database => (await database.Workspaces.FirstOrDefaultAsync(workspace => workspace.Id == id, cancellationToken)).ToOption(),
            cancellationToken);

    public Task<Option<Workspace>> FindAtAsync(string path, CancellationToken cancellationToken) =>
        RunAsync(
            async database => (await database.Workspaces.ToListAsync(cancellationToken)).FirstOrDefault(workspace => workspace.Location.IsAt(path)).ToOption(),
            cancellationToken);

    public Task<IReadOnlyList<Workspace>> AllAsync(CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<Workspace>>(async database => await database.Workspaces.ToListAsync(cancellationToken), cancellationToken);

    public Task RemoveAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                if (await database.Workspaces.FirstOrDefaultAsync(workspace => workspace.Id == id, cancellationToken) is { } found)
                {
                    database.Workspaces.Remove(found);
                }

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task RunAsync(CancellationToken cancellationToken) => owner.OpenedAsync(cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<T> RunAsync<T>(Func<WorkspacesDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        owner.RunAsync(work, cancellationToken);
}
