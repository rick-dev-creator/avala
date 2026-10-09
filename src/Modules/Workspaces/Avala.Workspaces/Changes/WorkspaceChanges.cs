using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Changes;

internal sealed class WorkspaceChanges(IWorkspaceStore store, IGitChanges git) : IWorkspaceChanges
{
    public async ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        await LoadAsync(workspace, cancellationToken)
            .BindAsync(found => HeadOfAsync(found, cancellationToken)
                .BindAsync(head => git.ChangedFilesAsync(found.Location.Repository, found.Base, head, cancellationToken)
                    .MapAsync(files => new WorkspaceDiff(workspace, found.Base.Value, head.Value, files))));

    public async ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
        await LoadAsync(workspace, cancellationToken)
            .BindAsync(found => HeadOfAsync(found, cancellationToken)
                .BindAsync(head => git.FileDiffAsync(found.Location.Repository, found.Base, head, path, cancellationToken)))
            .BindAsync(diff => diff.ToResult(WorkspaceFailure.FileUnchanged));

    public async ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        await LoadAsync(workspace, cancellationToken)
            .BindAsync(found => PlanAsync(found, cancellationToken))
            .MapAsync(plan => plan.Merged.Conflicts);

    public async ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken)
    {
        if (!(await LoadAsync(workspace, cancellationToken)).TryGetValue(out var found, out var failure)
            || !(await PlanAsync(found, cancellationToken)).TryGetValue(out var plan, out failure))
        {
            return failure;
        }

        if (plan.Merged.Conflicts.Count > 0)
        {
            return WorkspaceFailure.MergeConflict;
        }

        var repository = found.Location.Repository;

        if (!(await git.TreeOfAsync(repository, plan.Tip, cancellationToken)).TryGetValue(out var tipTree, out failure)
            || !(await git.CheckoutOfAsync(repository, plan.Target, cancellationToken)).TryGetValue(out var checkout, out failure))
        {
            return failure;
        }

        if (tipTree == plan.Merged.Tree)
        {
            return new MergedWork(workspace, plan.Target.Value, Option<string>.None, Option<string>.None);
        }

        if (!(await CleanAsync(checkout, cancellationToken)).TryGetValue(out _, out failure)
            || !(await git.CommitTreeAsync(repository, plan.Merged.Tree, plan.Tip, message, cancellationToken)).TryGetValue(out var commit, out failure))
        {
            return failure;
        }

        if (!await git.MoveBranchAsync(repository, plan.Target, commit, plan.Tip, cancellationToken))
        {
            return WorkspaceFailure.BaseMoved;
        }

        if (!await AdvanceAsync(checkout, plan.Tip, commit, cancellationToken))
        {
            _ = await git.MoveBranchAsync(repository, plan.Target, plan.Tip, commit, cancellationToken);

            return WorkspaceFailure.BaseCheckoutDirty;
        }

        return new MergedWork(workspace, plan.Target.Value, commit.Value, checkout);
    }

    private Task<Result<MergePlan, WorkspaceFailure>> PlanAsync(Workspace workspace, CancellationToken cancellationToken) =>
        workspace.BaseBranch.Match(
            target => PlanOntoAsync(workspace, target, cancellationToken),
            () => Task.FromResult(Result<MergePlan, WorkspaceFailure>.Failure(WorkspaceFailure.NoBaseBranch)));

    private async Task<Result<MergePlan, WorkspaceFailure>> PlanOntoAsync(Workspace workspace, BranchName target, CancellationToken cancellationToken)
    {
        var repository = workspace.Location.Repository;

        if (!(await HeadOfAsync(workspace, cancellationToken)).TryGetValue(out var head, out var failure)
            || !(await git.TipOfAsync(repository, target, cancellationToken)).Bind(found => found.ToResult(WorkspaceFailure.NoBaseBranch))
                .TryGetValue(out var tip, out failure))
        {
            return failure;
        }

        return await git.MergeTreeAsync(repository, workspace.Base, tip, head, cancellationToken)
            .MapAsync(merged => new MergePlan(target, tip, merged));
    }

    private async Task<Result<bool, WorkspaceFailure>> CleanAsync(Option<string> checkout, CancellationToken cancellationToken) =>
        await checkout.Match(
            folder => git.IsCleanAsync(folder, cancellationToken)
                .BindAsync(clean => clean ? Result<bool, WorkspaceFailure>.Success(true) : WorkspaceFailure.BaseCheckoutDirty),
            () => Task.FromResult(Result<bool, WorkspaceFailure>.Success(true)));

    private async Task<bool> AdvanceAsync(Option<string> checkout, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
        await checkout.Match(folder => git.AdvanceCheckoutAsync(folder, from, to, cancellationToken), () => Task.FromResult(true));

    private async Task<Result<CommitSha, WorkspaceFailure>> HeadOfAsync(Workspace workspace, CancellationToken cancellationToken) =>
        await git.TipOfAsync(workspace.Location.Repository, workspace.Branch, cancellationToken)
            .BindAsync(tip => tip.ToResult(WorkspaceFailure.GitFailed));

    private async Task<Result<Workspace, WorkspaceFailure>> LoadAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        (await store.FindAsync(id, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace);

    private sealed record MergePlan(BranchName Target, CommitSha Tip, MergedTree Merged);
}
