namespace Avala.Workspaces.Contracts;

public sealed record WorkspaceRequest(string RepositoryPath, string BaseRef = "HEAD");

public sealed record WorkspaceInfo(WorkspaceId Id, string Path, string Branch, string BaseCommit);

public sealed record CheckpointInfo(WorkspaceId Workspace, int Number, string Commit, string Label);

public enum WorkspaceFailure
{
    NotAGitRepository,
    BranchAlreadyExists,
    GitFailed,
    UnknownWorkspace,
    InvalidState,
}
