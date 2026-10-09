using System.Collections.Concurrent;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Testing;

public sealed class CommittedFiles : IBaseFiles
{
    public const string BaseCommit = "ba5eba5eba5eba5eba5eba5eba5eba5eba5eba5e";

    private readonly ConcurrentDictionary<string, Option<WorkspaceFailure>> worktrees = new();
    private readonly ConcurrentDictionary<(string Worktree, string Path), (string Content, bool Edited)> files = new();
    private readonly ConcurrentQueue<(string Worktree, string Path)> reads = new();

    public IReadOnlyList<(string Worktree, string Path)> Reads => [.. reads];

    public static FileOrigin Origin(bool editedInWorktree = false) => new(BaseCommit, editedInWorktree);

    public CommittedFiles Workspace(string worktree)
    {
        worktrees[worktree] = Option<WorkspaceFailure>.None;

        return this;
    }

    public CommittedFiles Failing(string worktree, WorkspaceFailure failure)
    {
        worktrees[worktree] = failure;

        return this;
    }

    public CommittedFiles With(string worktree, string path, string content, bool editedInWorktree = false)
    {
        files[(worktree, path)] = (content, editedInWorktree);

        return Workspace(worktree);
    }

    public ValueTask<Result<BaseFile, WorkspaceFailure>> ReadCurrentAsync(string repository, string path, CancellationToken cancellationToken) =>
        ReadAsync(repository, path, cancellationToken);

    public ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken)
    {
        reads.Enqueue((worktree, path));

        if (!worktrees.TryGetValue(worktree, out var failing))
        {
            return ValueTask.FromResult(Result<BaseFile, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));
        }

        return ValueTask.FromResult(failing.Match(
            Result<BaseFile, WorkspaceFailure>.Failure,
            () => files.TryGetValue((worktree, path), out var file)
                ? new BaseFile(path, Origin(file.Edited), file.Content)
                : new BaseFile(path, Origin(), Option<string>.None)));
    }
}
