using Avala.Sdk;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal sealed class WorkspaceShelf(IGit git, IGitChanges changes, IWorkspaceStore store, WorkspaceSettings settings) : IWorkspaceShelf
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
            return await ReleaseAsync(found, cancellationToken);
        }

        if (found.State != WorkspaceState.Ready)
        {
            return WorkspaceFailure.InvalidState;
        }

        var present = Directory.Exists(found.Location.Path);

        if (present && !(await CheckoutFreeableAsync(found, cancellationToken)).TryGetValue(out _, out failure))
        {
            return failure;
        }

        if (!(await KeepChangesAsync(found, present, cancellationToken)).TryGetValue(out _, out failure)
            || !(await KeepIgnoredAsync(found, present, cancellationToken)).TryGetValue(out _, out failure)
            || !(await git.KeepTipAsync(found.Location, found.Branch, found.Id, cancellationToken)).TryGetValue(out _, out failure)
            || !Valid(found.Shelve()).TryGetValue(out _, out failure))
        {
            return failure;
        }

        await store.SaveAsync(found, cancellationToken);

        return await ReleaseAsync(found, cancellationToken);
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

        RestoreIgnored(found);
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

    private async Task<Result<WorkspaceInfo, WorkspaceFailure>> ReleaseAsync(Workspace workspace, CancellationToken cancellationToken) =>
        await git.ReleaseWorktreeAsync(workspace.Location, workspace.Branch, cancellationToken).MapAsync(_ => workspace.Describe());

    private async Task<Result<bool, WorkspaceFailure>> CheckoutFreeableAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!(await git.HeadBranchAsync(workspace.Location, cancellationToken)).TryGetValue(out var head, out var failure))
        {
            return failure;
        }

        if (head == Option<BranchName>.Some(workspace.Branch))
        {
            return true;
        }

        if (head.IsNone)
        {
            return WorkspaceFailure.CheckoutLeftItsBranch;
        }

        return await git.HasChangesAsync(workspace.Location, cancellationToken)
            .BindAsync(changed => changed ? Result<bool, WorkspaceFailure>.Failure(WorkspaceFailure.CheckoutLeftItsBranch) : Result<bool, WorkspaceFailure>.Success(true));
    }

    private async Task<Result<bool, WorkspaceFailure>> KeepChangesAsync(Workspace workspace, bool present, CancellationToken cancellationToken)
    {
        if (!present)
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

    private async Task<Result<bool, WorkspaceFailure>> KeepIgnoredAsync(Workspace workspace, bool present, CancellationToken cancellationToken)
    {
        if (!present)
        {
            return false;
        }

        if (!(await git.IgnoredAsync(workspace.Location, cancellationToken)).TryGetValue(out var ignored, out var failure))
        {
            return failure;
        }

        try
        {
            foreach (var entry in ignored.Where(entry => !BuildOutputs.Holds(entry)))
            {
                Move(Path.Combine(workspace.Location.Path, entry), Path.Combine(KeptOf(workspace), entry));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailure.FileUnwritable;
        }

        return true;
    }

    private void RestoreIgnored(Workspace workspace)
    {
        var kept = KeptOf(workspace);

        if (!Directory.Exists(kept))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(kept))
        {
            Move(entry, Path.Combine(workspace.Location.Path, Path.GetRelativePath(kept, entry)));
        }

        Directory.Delete(kept, recursive: true);
    }

    private string KeptOf(Workspace workspace) => Path.Combine(settings.Kept, $"{workspace.Id.Value:N}");

    private static void Move(string from, string to)
    {
        var source = Path.TrimEndingDirectorySeparator(from);
        var target = Path.TrimEndingDirectorySeparator(to);
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? target);

        if (Directory.Exists(source))
        {
            Directory.Move(source, target);
        }
        else if (File.Exists(source))
        {
            File.Move(source, target, overwrite: true);
        }
    }

    private async Task<Result<Workspace, WorkspaceFailure>> LoadAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        (await store.FindAsync(id, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace);

    private static Result<T, WorkspaceFailure> Valid<T>(Result<T, WorkspaceError> result) =>
        result.MapError(_ => WorkspaceFailure.InvalidState);
}
