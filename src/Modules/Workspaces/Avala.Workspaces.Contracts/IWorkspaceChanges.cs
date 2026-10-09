using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IWorkspaceChanges
{
    ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken);

    ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken);
}

public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
}

public sealed record FileChange(string Path, ChangeKind Kind, Option<int> Added, Option<int> Removed);

public sealed record WorkspaceDiff(WorkspaceId Workspace, string BaseCommit, string Head, IReadOnlyList<FileChange> Files);

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

public sealed record DiffLine(DiffLineKind Kind, string Text);

public sealed record DiffHunk(int OldStart, int OldLines, int NewStart, int NewLines, string Section, IReadOnlyList<DiffLine> Lines);

public sealed record FileDiff(string Path, bool Binary, IReadOnlyList<DiffHunk> Hunks);

public sealed record MergedWork(WorkspaceId Workspace, string BaseBranch, Option<string> Commit, Option<string> Checkout);
