using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Avala.Workspaces.Storage;

internal sealed class SqliteWorkspaceStore(AvalaPaths paths) : IWorkspaceStore, IAsyncDisposable
{
    private readonly SemaphoreSlim turn = new(1, 1);
    private WorkspacesDbContext? context;

    public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken) =>
        RunAsync(
            async database =>
            {
                if (database.Entry(workspace).State == EntityState.Detached)
                {
                    await database.Workspaces.AddAsync(workspace, cancellationToken);
                }

                return await database.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

    public Task<Option<Workspace>> FindAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        RunAsync(
            async database => (await database.Workspaces.FirstOrDefaultAsync(workspace => workspace.Id == id, cancellationToken)).ToOption(),
            cancellationToken);

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

    public async ValueTask DisposeAsync()
    {
        if (context is not null)
        {
            await context.DisposeAsync();
        }

        turn.Dispose();
    }

    private async Task<T> RunAsync<T>(Func<WorkspacesDbContext, Task<T>> work, CancellationToken cancellationToken)
    {
        await turn.WaitAsync(cancellationToken);

        try
        {
            return await Task.Run(async () => await work(await OpenAsync(cancellationToken)), cancellationToken);
        }
        finally
        {
            turn.Release();
        }
    }

    private async Task<WorkspacesDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(paths.Data);
            context = new WorkspacesDbContext(paths.Database("workspaces"));
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        return context;
    }
}
