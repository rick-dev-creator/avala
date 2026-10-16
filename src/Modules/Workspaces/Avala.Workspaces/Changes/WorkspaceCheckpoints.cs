using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Changes;

internal sealed class WorkspaceCheckpoints(IWorkspaceStore store, IWorkspaces workspaces, IGitChanges changes, IGit git) : IWorkspaceCheckpoints
{
    public async ValueTask<Result<IReadOnlyList<CheckpointChanges>, WorkspaceFailure>> ListAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (!(await store.FindAsync(workspace, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace).TryGetValue(out var found, out var missing))
        {
            return missing;
        }

        var listed = new List<CheckpointChanges>();
        var previous = found.Base;

        foreach (var checkpoint in found.Checkpoints)
        {
            if (!(await changes.ChangedFilesAsync(found.Location.Repository, previous, checkpoint.Commit, cancellationToken)).TryGetValue(out var files, out var failure))
            {
                return failure;
            }

            listed.Add(new CheckpointChanges(checkpoint.Number, checkpoint.Label, checkpoint.Commit.Value, files));
            previous = checkpoint.Commit;
        }

        return listed;
    }

    public async ValueTask<Result<CheckpointChanges, WorkspaceFailure>> RestoreAsync(WorkspaceId workspace, int checkpoint, CancellationToken cancellationToken)
    {
        if (!(await store.FindAsync(workspace, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace).TryGetValue(out var found, out var missing))
        {
            return missing;
        }

        if (found.Checkpoints.FirstOrDefault(taken => taken.Number == checkpoint) is not { } target)
        {
            return WorkspaceFailure.UnknownCheckpoint;
        }

        if (!(await workspaces.CheckpointAsync(workspace, $"Before restoring to {target.Label}", cancellationToken)).TryGetValue(out var before, out var failure)
            || !(await git.ResetTreeAsync(found.Location, target.Commit, cancellationToken)).TryGetValue(out _, out failure)
            || !(await workspaces.CheckpointAsync(workspace, $"Restored to {target.Label}", cancellationToken)).TryGetValue(out var restored, out failure)
            || !Commits(before.Commit, restored.Commit).TryGetValue(out var commits, out failure)
            || !(await changes.ChangedFilesAsync(found.Location.Repository, commits.From, commits.To, cancellationToken)).TryGetValue(out var files, out failure))
        {
            return failure;
        }

        return new CheckpointChanges(restored.Number, restored.Label, restored.Commit, files);
    }

    private static Result<(CommitSha From, CommitSha To), WorkspaceFailure> Commits(string from, string to) =>
        CommitSha.Create(from).Bind(before => CommitSha.Create(to).Map(after => (before, after))).MapError(_ => WorkspaceFailure.GitFailed);
}
