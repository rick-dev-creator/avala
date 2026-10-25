using System.Text;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Testing;

internal sealed class InMemoryWorkspaces(InMemoryGit git, string root) : IWorkspaces, IWorkspaceCheckpoints, IWorkspaceShelf, IBaseFiles
{
    private const string GitEntry = ".git";

    private const string KeptLabel = "Kept when its worktree was freed";

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken)
    {
        var top = git.RootOf(request.RepositoryPath).Match(found => found, () => string.Empty);

        if (top.Length == 0)
        {
            return WorkspaceFailure.NotAGitRepository;
        }

        var nested = git.WorkspaceAt(top);
        var repository = nested.Match(parent => parent.Repository, () => top);

        var resolved = request.BaseRef == "HEAD" && nested.IsSome
            ? nested.Map(parent => Commit(parent.Head))
            : await git.ResolveAsync(repository, request.BaseRef, cancellationToken);
        var rules = await request.Rules.Match(
            reference => git.ResolveAsync(repository, reference, cancellationToken),
            () => Task.FromResult(resolved));

        if (resolved.Match<GitCommit?>(found => found, () => null) is not { } commit || rules.IsNone)
        {
            return WorkspaceFailure.GitFailed;
        }

        var id = WorkspaceId.New();
        var path = Path.Combine(InMemoryGit.Canonical(root), $"{id.Value:N}");
        await CheckOutAsync(commit, path, repository, id, cancellationToken);
        var info = new WorkspaceInfo(id, path, $"avala/{id.Value:N}", commit.Id)
        {
            BaseBranch = request.BaseRef == "HEAD" && nested.IsSome ? nested.Map(parent => parent.Info.Branch)
                : request.BaseRef is "HEAD" or InMemoryGit.MainBranch ? InMemoryGit.MainBranch
                : git.OnBranch(repository, request.BaseRef).Map(found => found.Info.Branch),
            RulesCommit = rules.Match(found => found.Id, () => commit.Id),
        };
        git.Hold(new HeldWorkspace(info, repository, [], Option<string>.None));

        return info;
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(git.Workspace(workspace).Map(found => found.Info).ToResult(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) =>
        ValueTask.FromResult(git.WorkspaceAt(folder).Map(found => found.Info).ToResult(WorkspaceFailure.UnknownWorkspace));

    public async ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) =>
        await git.Workspace(workspace).Match<Task<Result<CheckpointInfo, WorkspaceFailure>>>(
            async found => await CheckpointAsync(found, label, cancellationToken),
            () => Task.FromResult<Result<CheckpointInfo, WorkspaceFailure>>(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(git.Workspace(workspace).ToResult(WorkspaceFailure.UnknownWorkspace).Map(found =>
        {
            Delete(found.Info.Path);
            git.Forget(workspace);

            return workspace;
        }));

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken)
    {
        var known = git.Workspaces();
        var folder = InMemoryGit.Canonical(root);
        var folders = Directory.Exists(folder) ? Directory.GetDirectories(folder).Select(InMemoryGit.Canonical) : [];

        return ValueTask.FromResult(new WorktreeReconciliation(
            [.. folders.Where(found => known.All(workspace => workspace.Info.Path != found)).Order(StringComparer.Ordinal)],
            [.. known.Where(workspace => !workspace.Info.Shelved && !Directory.Exists(workspace.Info.Path)).Select(workspace => workspace.Info)]));
    }

    public async ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken)
    {
        var current = await ReconcileAsync(cancellationToken);
        var strays = current.Strays.Where(stray => found.Strays.Contains(stray, StringComparer.Ordinal)).ToList();
        var missing = current.Missing.Where(gone => found.Missing.Any(listed => listed.Id == gone.Id)).ToList();
        strays.ForEach(Delete);
        missing.ForEach(gone => git.Forget(gone.Id));

        return new WorktreeReconciliation(strays, missing);
    }

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> ShelveAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (git.Workspace(workspace).Match<HeldWorkspace?>(held => held, () => null) is not { } found)
        {
            return WorkspaceFailure.UnknownWorkspace;
        }

        if (found.Info.Shelved)
        {
            return found.Info;
        }

        if (Directory.Exists(found.Info.Path))
        {
            var files = await FolderSnapshot.ReadAsync(found.Info.Path, Commit(found.Head).Files, cancellationToken);

            if (!Commit(found.Head).Files.SameAs(files))
            {
                _ = await CheckpointAsync(found, KeptLabel, cancellationToken);
            }

            Delete(found.Info.Path);
        }

        var current = git.Workspace(workspace).Match(held => held, () => found);

        return git.Hold(current with { Info = current.Info with { Shelved = true } }).Info;
    }

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> RebuildAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (git.Workspace(workspace).Match<HeldWorkspace?>(held => held, () => null) is not { } found)
        {
            return WorkspaceFailure.UnknownWorkspace;
        }

        if (!found.Info.Shelved)
        {
            return found.Info;
        }

        if (Directory.Exists(found.Info.Path))
        {
            return WorkspaceFailure.GitFailed;
        }

        await CheckOutAsync(Commit(found.Head), found.Info.Path, found.Repository, workspace, cancellationToken);

        return git.Hold(found with { Info = found.Info with { Shelved = false } }).Info;
    }

    public async ValueTask<Result<bool, WorkspaceFailure>> DeliveredAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (git.Workspace(workspace).Match<HeldWorkspace?>(held => held, () => null) is not { } found)
        {
            return WorkspaceFailure.UnknownWorkspace;
        }

        if (found.LastMerged == Option<string>.Some(found.Head))
        {
            return true;
        }

        var target = await found.Info.BaseBranch.Match(
            async branch => branch == InMemoryGit.MainBranch
                ? Option<string>.Some((await git.HeadOfAsync(found.Repository, cancellationToken)).Id)
                : git.OnBranch(found.Repository, branch).Map(parent => parent.Head),
            () => Task.FromResult(Option<string>.None));

        return target.Match(tip => Reaches(tip, found.Head), () => false);
    }

    public ValueTask<Result<IReadOnlyList<CheckpointChanges>, WorkspaceFailure>> ListAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(git.Workspace(workspace).ToResult(WorkspaceFailure.UnknownWorkspace).Map(found =>
        {
            var listed = new List<CheckpointChanges>();
            var previous = found.Info.BaseCommit;

            foreach (var checkpoint in found.Checkpoints)
            {
                listed.Add(new CheckpointChanges(checkpoint.Number, checkpoint.Label, checkpoint.Commit, Changes(previous, checkpoint.Commit)));
                previous = checkpoint.Commit;
            }

            return (IReadOnlyList<CheckpointChanges>)listed;
        }));

    public async ValueTask<Result<CheckpointChanges, WorkspaceFailure>> RestoreAsync(WorkspaceId workspace, int checkpoint, CancellationToken cancellationToken)
    {
        if (git.Workspace(workspace).Match<HeldWorkspace?>(held => held, () => null) is not { } found)
        {
            return WorkspaceFailure.UnknownWorkspace;
        }

        if (found.Checkpoints.FirstOrDefault(taken => taken.Number == checkpoint) is not { } target)
        {
            return WorkspaceFailure.UnknownCheckpoint;
        }

        var before = await CheckpointAsync(found, $"Before restoring to {target.Label}", cancellationToken);
        await Commit(before.Commit).Files.ApplyAsync(Commit(target.Commit).Files, found.Info.Path, cancellationToken);
        var restored = await CheckpointAsync(git.Workspace(workspace).Match(held => held, () => found), $"Restored to {target.Label}", cancellationToken);

        return new CheckpointChanges(restored.Number, restored.Label, restored.Commit, Changes(before.Commit, restored.Commit));
    }

    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken) =>
        await git.WorkspaceAt(worktree).Match<Task<Result<BaseFile, WorkspaceFailure>>>(
            async found => found.Info.Shelved
                ? Read(Commit(found.Info.RulesCommit), path, Commit(found.Head).Files.Entry(path))
                : await ReadAsync(Commit(found.Info.RulesCommit), found.Info.Path, path, cancellationToken),
            () => Task.FromResult<Result<BaseFile, WorkspaceFailure>>(WorkspaceFailure.UnknownWorkspace));

    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadCurrentAsync(string repository, string path, CancellationToken cancellationToken) =>
        await git.RootOf(repository).Match<Task<Result<BaseFile, WorkspaceFailure>>>(
            async found => await ReadAsync(await git.HeadOfAsync(found, cancellationToken), found, path, cancellationToken),
            () => Task.FromResult<Result<BaseFile, WorkspaceFailure>>(WorkspaceFailure.NotAGitRepository));

    internal IReadOnlyList<FileChange> Changes(string from, string to)
    {
        var (before, after) = (Commit(from).Files, Commit(to).Files);

        return
        [
            .. before.Files.Keys.Union(after.Files.Keys, StringComparer.Ordinal).Order(GitPathOrder.Instance)
                .SelectMany(file => LineDiff.Change(file, before, after).Match<FileChange[]>(change => [change], () => [])),
        ];
    }

    internal GitCommit Commit(string id) => git.Find(id).Match(found => found, () => throw new InvalidOperationException($"Unknown commit {id}."));

    private async Task<CheckpointInfo> CheckpointAsync(HeldWorkspace workspace, string label, CancellationToken cancellationToken)
    {
        var files = await FolderSnapshot.ReadAsync(workspace.Info.Path, Commit(workspace.Head).Files, cancellationToken);

        if (workspace.Checkpoints is [.., var last] && last.Label == label && Commit(last.Commit).Files.SameAs(files))
        {
            return last;
        }

        var commit = git.Record(files, label, workspace.Head);
        var checkpoint = new CheckpointInfo(workspace.Info.Id, workspace.Checkpoints.Count + 1, commit.Id, label);
        git.Hold(workspace with { Checkpoints = [.. workspace.Checkpoints, checkpoint], Tip = commit.Id });

        return checkpoint;
    }

    private static async Task<Result<BaseFile, WorkspaceFailure>> ReadAsync(GitCommit commit, string folder, string path, CancellationToken cancellationToken) =>
        Read(commit, path, await FolderSnapshot.ReadEntryAsync(folder, path, cancellationToken));

    private static Result<BaseFile, WorkspaceFailure> Read(GitCommit commit, string path, Option<byte[]> current)
    {
        var committed = commit.Files.Entry(path);

        return new BaseFile(path, new FileOrigin(commit.Id, !FolderSnapshot.Same(committed, current)), committed.Map(Encoding.UTF8.GetString));
    }

    private bool Reaches(string from, string commit)
    {
        var current = Option<string>.Some(from);

        while (current.Match<string?>(found => found, () => null) is { } id)
        {
            if (id == commit)
            {
                return true;
            }

            current = git.Find(id).Bind(found => found.Parent);
        }

        return false;
    }

    private static async Task CheckOutAsync(GitCommit commit, string path, string repository, WorkspaceId id, CancellationToken cancellationToken)
    {
        await commit.Files.WriteToAsync(path, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(path, GitEntry), $"gitdir: {Path.Combine(repository, GitEntry, "worktrees", $"{id.Value:N}")}\n", cancellationToken);
    }

    private static void Delete(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
