using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

internal sealed class InMemoryChanges(InMemoryGit git, InMemoryWorkspaces workspaces) : IWorkspaceChanges
{
    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Held(workspace).Map(found => new WorkspaceDiff(workspace, found.Info.BaseCommit, found.Head, workspaces.Changes(found.Info.BaseCommit, found.Head))));

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Held(workspace).Bind(found => LineDiff.Of(path, workspaces.Commit(found.Info.BaseCommit).Files, workspaces.Commit(found.Head).Files)
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

        if (!await plan.Tip.Files.TrackedUnchangedAsync(plan.Checkout, cancellationToken) || await OverwritesUntrackedAsync(plan, cancellationToken))
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
        await plan.Tip.Files.ApplyAsync(plan.Merged, plan.Checkout, cancellationToken);
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
        var touched = mergeBase.Files.Keys.Union(work.Files.Keys, StringComparer.Ordinal).Where(path => !FolderSnapshot.SameEntry(mergeBase, work, path)).ToList();
        var merged = tip.Files;
        var conflicts = new List<string>();

        foreach (var path in touched)
        {
            if (FolderSnapshot.SameEntry(mergeBase, tip.Files, path) || FolderSnapshot.SameEntry(work, tip.Files, path))
            {
                merged = merged.WithEntryOf(work, path);
            }
            else if (Combined(mergeBase, tip.Files, work, path).Match<byte[]?>(content => content, () => null) is { } content)
            {
                merged = merged.With(path, content, work.ModeOf(path) == mergeBase.ModeOf(path) ? tip.Files.ModeOf(path) : work.ModeOf(path));
            }
            else
            {
                conflicts.Add(path);
            }
        }

        return new MergePlan(workspace, tip, merged, [.. conflicts.Order(GitPathOrder.Instance)])
        {
            Branch = branch,
            Target = target,
            Checkout = checkout,
        };
    }

    private static async Task<bool> OverwritesUntrackedAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        foreach (var path in plan.Merged.Files.Keys.Where(path => !plan.Tip.Files.Files.ContainsKey(path)))
        {
            if ((await FolderSnapshot.ReadEntryAsync(plan.Checkout, path, cancellationToken)).IsSome)
            {
                return true;
            }
        }

        return false;
    }

    private static Option<byte[]> Combined(FolderSnapshot ancestor, FolderSnapshot ours, FolderSnapshot theirs, string path) =>
        ancestor.Entry(path).Bind(common => ours.Entry(path).Bind(mine => theirs.Entry(path).Bind(other => LineDiff.Merge(common, mine, other))));

    private Result<HeldWorkspace, WorkspaceFailure> Held(WorkspaceId workspace) => git.Workspace(workspace).ToResult(WorkspaceFailure.UnknownWorkspace);

    private sealed record MergePlan(HeldWorkspace Workspace, GitCommit Tip, FolderSnapshot Merged, IReadOnlyList<string> Conflicts)
    {
        public required string Branch { get; init; }

        public required Option<HeldWorkspace> Target { get; init; }

        public required string Checkout { get; init; }
    }
}
