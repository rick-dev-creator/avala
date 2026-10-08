using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal interface IGit
{
    Task<Result<string, WorkspaceFailure>> FindRepositoryRootAsync(string path, CancellationToken cancellationToken);

    Task<Result<bool, WorkspaceFailure>> BranchExistsAsync(string repository, BranchName branch, CancellationToken cancellationToken);

    Task<Result<WorkspaceLocation, WorkspaceFailure>> AddWorktreeAsync(WorkspaceLocation location, BranchName branch, string baseRef, CancellationToken cancellationToken);

    Task<Result<CommitSha, WorkspaceFailure>> CommitAllAsync(WorkspaceLocation location, string label, CancellationToken cancellationToken);

    Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveWorktreeAsync(WorkspaceLocation location, BranchName branch, CancellationToken cancellationToken);
}
