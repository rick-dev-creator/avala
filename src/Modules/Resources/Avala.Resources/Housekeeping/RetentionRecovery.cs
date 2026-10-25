using Avala.Jobs.Contracts;
using Avala.Resources.History;
using Avala.Resources.Tracking;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Housekeeping;

internal sealed class RetentionRecovery(IJobCatalog catalog, IWorkspaces workspaces, ResourceTracker tracker, IWorktreeHistoryStore history) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var job in (await catalog.ListAsync(cancellationToken)).Where(job => job.Status.Ended() || job.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp))
        {
            var found = await job.Workspace.Match(
                workspace => workspaces.FindAsync(workspace, cancellationToken).AsTask(),
                () => Task.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

            if (found.TryGetValue(out var worktree, out _))
            {
                await tracker.RecoverAsync(job, worktree.Path, job.Status.Ended() ? Option<DateTimeOffset>.None : await history.RestingSinceAsync(job.Job, cancellationToken), cancellationToken);
            }
        }
    }
}
