using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

internal sealed class InMemoryChanges(InMemoryGit git, InMemoryWorkspaces workspaces) : IWorkspaceChanges
{
    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Held(workspace).Map(found => new WorkspaceDiff(workspace, found.Info.BaseCommit, found.Head, workspaces.Changes(found.Info.BaseCommit, found.Head))));

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Held(workspace).Bind(found => LineDiff.Of(
                path,
                workspaces.Commit(found.Info.BaseCommit).Files.Entry(path),
                workspaces.Commit(found.Head).Files.Entry(path))
            .ToResult(WorkspaceFailure.FileUnchanged)));

    public async ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        (await PlanAsync(workspace, cancellationToken)).Map(plan => plan.Conflicts);

    public async ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken)
    {
        if (!(await PlanAsync(workspace, cancellationToken)).TryGetValue(out var plan, out var failure))
        {
            return failure;
        }

        if (plan.Conflicts.Count > 0)
        {
            return WorkspaceFailure.MergeConflict;
        }

        var held = plan.Workspace;

        if (plan.Merged.SameAs(plan.Tip.Files))
        {
            git.Hold(held with { LastMerged = held.Head });

            return new MergedWork(held.Info.Id, InMemoryGit.MainBranch, Option<string>.None, Option<string>.None);
        }

        if (!(await FolderSnapshot.ReadAsync(held.Repository, cancellationToken)).SameAs(plan.Tip.Files))
        {
            return WorkspaceFailure.BaseCheckoutDirty;
        }

        var commit = git.Record(plan.Merged, message, plan.Tip.Id);
        git.Advance(held.Repository, commit.Id);
        await plan.Merged.WriteToAsync(held.Repository, cancellationToken);
        git.Hold(held with { LastMerged = held.Head });

        return new MergedWork(held.Info.Id, InMemoryGit.MainBranch, commit.Id, held.Repository);
    }

    private async Task<Result<MergePlan, WorkspaceFailure>> PlanAsync(WorkspaceId id, CancellationToken cancellationToken)
    {
        if (git.Workspace(id).Match<HeldWorkspace?>(found => found, () => null) is not { } workspace)
        {
            return WorkspaceFailure.UnknownWorkspace;
        }

        if (workspace.Info.BaseBranch.IsNone)
        {
            return WorkspaceFailure.NoBaseBranch;
        }

        var tip = await git.HeadOfAsync(workspace.Repository, cancellationToken);
        var work = workspaces.Commit(workspace.Head).Files;
        var mergeBase = workspaces.Commit(workspace.LastMerged.Match(merged => merged, () => workspace.Info.BaseCommit)).Files;
        var touched = mergeBase.Files.Keys.Union(work.Files.Keys, StringComparer.Ordinal).Where(path => !FolderSnapshot.Same(mergeBase.Entry(path), work.Entry(path))).ToList();
        var conflicts = touched
            .Where(path => !FolderSnapshot.Same(mergeBase.Entry(path), tip.Files.Entry(path)) && !FolderSnapshot.Same(work.Entry(path), tip.Files.Entry(path)))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new MergePlan(workspace, tip, touched.Aggregate(tip.Files, (files, path) => files.With(path, work.Entry(path))), conflicts);
    }

    private Result<HeldWorkspace, WorkspaceFailure> Held(WorkspaceId workspace) => git.Workspace(workspace).ToResult(WorkspaceFailure.UnknownWorkspace);

    private sealed record MergePlan(HeldWorkspace Workspace, GitCommit Tip, FolderSnapshot Merged, IReadOnlyList<string> Conflicts);
}
