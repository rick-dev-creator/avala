using Avala.Jobs.Contracts;
using Avala.Resources.Reaping;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Housekeeping;

internal sealed class RetentionRecovery(IJobCatalog catalog, IWorkspaces workspaces, WorktreeHousekeeper housekeeper, OrphanReaper reaper) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var job in (await catalog.ListAsync(cancellationToken)).Where(job => job.Status.Ended()))
        {
            var found = await job.Workspace.Match(
                workspace => workspaces.FindAsync(workspace, cancellationToken).AsTask(),
                () => Task.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

            if (found.TryGetValue(out var worktree, out _))
            {
                await housekeeper.RetainAsync(job.Job, job.Status, [worktree.Path], job.Ended.Match(at => at, () => job.Submitted), reaper, cancellationToken);
            }
        }
    }
}
