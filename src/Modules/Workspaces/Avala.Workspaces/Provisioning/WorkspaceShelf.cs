using Avala.Sdk;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal sealed class WorkspaceShelf(IGit git, IGitChanges changes, IWorkspaceStore store) : IWorkspaceShelf
{
    public const string KeptLabel = "Kept when its worktree was freed";

    private const string Heads = "refs/heads/";

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> ShelveAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (!(await LoadAsync(workspace, cancellationToken)).TryGetValue(out var found, out var failure))
        {
            return failure;
        }

        if (found.IsShelved)
        {
            return found.Describe();
        }

        if (found.State != WorkspaceState.Ready)
        {
            return WorkspaceFailure.InvalidState;
        }

        if (!(await KeepChangesAsync(found, cancellationToken)).TryGetValue(out _, out failure)
            || !(await git.ShelveWorktreeAsync(found.Location, found.Branch, found.Id, cancellationToken)).TryGetValue(out _, out failure)
            || !Valid(found.Shelve()).TryGetValue(out _, out failure))
        {
            return failure;
        }

        await store.SaveAsync(found, cancellationToken);

        return found.Describe();
    }

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> RebuildAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (!(await LoadAsync(workspace, cancellationToken)).TryGetValue(out var found, out var failure))
        {
            return failure;
        }

        if (!found.IsShelved)
        {
            return found.State == WorkspaceState.Ready ? found.Describe() : WorkspaceFailure.InvalidState;
        }

        if (!(await git.RebuildWorktreeAsync(found.Location, found.Branch, found.Id, cancellationToken)).TryGetValue(out _, out failure)
            || !Valid(found.Rebuild()).TryGetValue(out _, out failure))
        {
            return failure;
        }

        await store.SaveAsync(found, cancellationToken);

        return found.Describe();
    }

    public async ValueTask<Result<bool, WorkspaceFailure>> DeliveredAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (!(await LoadAsync(workspace, cancellationToken)).TryGetValue(out var found, out var failure)
            || !(await changes.WorkOfAsync(found, cancellationToken)).TryGetValue(out var work, out failure)
            || !(await changes.LastMergedAsync(found.Location.Repository, found.Id, cancellationToken)).TryGetValue(out var merged, out failure))
        {
            return failure;
        }

        if (merged == Option<CommitSha>.Some(work))
        {
            return true;
        }

        return await found.BaseBranch.Match(
            async target => await git.ContainsAsync(found.Location.Repository, $"{Heads}{target.Value}", work, cancellationToken)
                || await git.ContainsAsync(found.Location.Repository, $"{target.Value}@{{upstream}}", work, cancellationToken),
            () => Task.FromResult(false));
    }

    private async Task<Result<bool, WorkspaceFailure>> KeepChangesAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspace.Location.Path))
        {
            return false;
        }

        if (!(await git.HasChangesAsync(workspace.Location, cancellationToken)).TryGetValue(out var changed, out var failure))
        {
            return failure;
        }

        if (!changed)
        {
            return false;
        }

        return await git.CommitAllAsync(workspace.Location, KeptLabel, cancellationToken)
            .BindAsync(commit => Valid(workspace.RecordCheckpoint(commit, KeptLabel)))
            .MapAsync(_ => true);
    }

    private async Task<Result<Workspace, WorkspaceFailure>> LoadAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        (await store.FindAsync(id, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace);

    private static Result<T, WorkspaceFailure> Valid<T>(Result<T, WorkspaceError> result) =>
        result.MapError(_ => WorkspaceFailure.InvalidState);
}
