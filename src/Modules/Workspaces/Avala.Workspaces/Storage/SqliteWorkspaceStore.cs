using Avala.Sdk;
using Avala.Storage;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Avala.Workspaces.Storage;

internal sealed class SqliteWorkspaceStore(AvalaPaths paths) : IWorkspaceStore, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();
    private WorkspacesDbContext? context;

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

    public Task RunAsync(CancellationToken cancellationToken) => RunAsync(_ => Task.FromResult(true), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private Task<T> RunAsync<T>(Func<WorkspacesDbContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    private async Task<WorkspacesDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new WorkspacesDbContext(paths.Database("workspaces"));
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            await ModuleDatabase.MigrateAsync(context, cancellationToken);
        }

        return context;
    }
}
