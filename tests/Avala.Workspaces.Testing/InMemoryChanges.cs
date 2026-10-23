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

            return new MergedWork(held.Info.Id, plan.Branch, Option<string>.None, Option<string>.None);
        }

        if (!(await FolderSnapshot.ReadAsync(plan.Checkout, cancellationToken)).SameAs(plan.Tip.Files))
        {
            return WorkspaceFailure.BaseCheckoutDirty;
        }

        var commit = git.Record(plan.Merged, message, plan.Tip.Id);
        plan.Target.Match(
            target => git.Hold(target with { Tip = commit.Id }),
            () =>
            {
                git.Advance(held.Repository, commit.Id);

                return held;
            });
        await plan.Merged.WriteToAsync(plan.Checkout, cancellationToken);
        git.Hold(git.Workspace(held.Info.Id).Match(current => current, () => held) with { LastMerged = held.Head });

        return new MergedWork(held.Info.Id, plan.Branch, commit.Id, plan.Checkout);
    }

    private async Task<Result<MergePlan, WorkspaceFailure>> PlanAsync(WorkspaceId id, CancellationToken cancellationToken)
    {
        if (git.Workspace(id).Match<HeldWorkspace?>(found => found, () => null) is not { } workspace)
        {
            return WorkspaceFailure.UnknownWorkspace;
        }

        var branch = workspace.Info.BaseBranch.Match(found => found, () => string.Empty);
        var target = branch == InMemoryGit.MainBranch ? Option<HeldWorkspace>.None : git.OnBranch(workspace.Repository, branch);

        if (branch.Length == 0 || (branch != InMemoryGit.MainBranch && target.IsNone))
        {
            return WorkspaceFailure.NoBaseBranch;
        }

        var tip = await target.Match(found => Task.FromResult(workspaces.Commit(found.Head)), () => git.HeadOfAsync(workspace.Repository, cancellationToken));
        var checkout = target.Match(found => found.Info.Path, () => workspace.Repository);
        var work = workspaces.Commit(workspace.Head).Files;
        var mergeBase = workspaces.Commit(workspace.LastMerged.Match(merged => merged, () => workspace.Info.BaseCommit)).Files;
        var touched = mergeBase.Files.Keys.Union(work.Files.Keys, StringComparer.Ordinal).Where(path => !FolderSnapshot.Same(mergeBase.Entry(path), work.Entry(path))).ToList();
        var conflicts = touched
            .Where(path => !FolderSnapshot.Same(mergeBase.Entry(path), tip.Files.Entry(path)) && !FolderSnapshot.Same(work.Entry(path), tip.Files.Entry(path)))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new MergePlan(workspace, tip, touched.Aggregate(tip.Files, (files, path) => files.With(path, work.Entry(path))), conflicts)
        {
            Branch = branch,
            Target = target,
            Checkout = checkout,
        };
    }

    private Result<HeldWorkspace, WorkspaceFailure> Held(WorkspaceId workspace) => git.Workspace(workspace).ToResult(WorkspaceFailure.UnknownWorkspace);

    private sealed record MergePlan(HeldWorkspace Workspace, GitCommit Tip, FolderSnapshot Merged, IReadOnlyList<string> Conflicts)
    {
        public required string Branch { get; init; }

        public required Option<HeldWorkspace> Target { get; init; }

        public required string Checkout { get; init; }
    }
}
