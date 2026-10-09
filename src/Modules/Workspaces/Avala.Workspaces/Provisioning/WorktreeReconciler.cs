using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal sealed class WorktreeReconciler(IGit git, IWorkspaceStore store, WorkspaceSettings settings)
{
    public async Task<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken)
    {
        var known = await store.AllAsync(cancellationToken);
        var folders = Directory.Exists(settings.Root) ? Directory.GetDirectories(settings.Root) : [];

        return new WorktreeReconciliation(
            [.. folders.Where(folder => !known.Any(workspace => workspace.Location.IsAt(folder))).Order(StringComparer.Ordinal)],
            [.. known.Where(workspace => !Directory.Exists(workspace.Location.Path)).Select(workspace => workspace.Describe())]);
    }

    public async Task<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken)
    {
        var current = await ReconcileAsync(cancellationToken);
        var strays = current.Strays.Where(stray => found.Strays.Contains(stray, StringComparer.Ordinal)).ToList();
        var missing = current.Missing.Where(gone => found.Missing.Any(listed => listed.Id == gone.Id)).ToList();

        await Task.Run(() => strays.ForEach(Delete), cancellationToken);

        foreach (var gone in missing)
        {
            await (await store.FindAsync(gone.Id, cancellationToken)).Match(
                workspace => ForgetAsync(workspace, cancellationToken),
                () => Task.CompletedTask);
        }

        return new WorktreeReconciliation(strays, missing);
    }

    private async Task ForgetAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        _ = await git.PruneWorktreesAsync(workspace.Location.Repository, cancellationToken);
        _ = workspace.Remove();
        await store.RemoveAsync(workspace.Id, cancellationToken);
    }

    private static void Delete(string folder)
    {
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(folder, recursive: true);
    }
}

internal static class WorkspaceDescriptions
{
    extension(Workspace workspace)
    {
        public WorkspaceInfo Describe() =>
            new(workspace.Id, workspace.Location.Path, workspace.Branch.Value, workspace.Base.Value)
            {
                BaseBranch = workspace.BaseBranch.Map(branch => branch.Value),
            };
    }
}
