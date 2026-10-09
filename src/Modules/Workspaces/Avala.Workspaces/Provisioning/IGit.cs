using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal interface IGit
{
    Task<Result<string, WorkspaceFailure>> FindRepositoryRootAsync(string path, CancellationToken cancellationToken);

    Task<Result<CommitSha, WorkspaceFailure>> ResolveCommitAsync(string repository, string reference, CancellationToken cancellationToken);

    Task<Result<bool, WorkspaceFailure>> BranchExistsAsync(string repository, BranchName branch, CancellationToken cancellationToken);

    Task<Result<WorkspaceLocation, WorkspaceFailure>> AddWorktreeAsync(WorkspaceLocation location, BranchName branch, CommitSha commit, CancellationToken cancellationToken);

    Task<Result<CommitSha, WorkspaceFailure>> CommitAllAsync(WorkspaceLocation location, string label, CancellationToken cancellationToken);

    Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveWorktreeAsync(WorkspaceLocation location, BranchName branch, CancellationToken cancellationToken);

    Task<Result<Option<string>, WorkspaceFailure>> CommittedBlobAsync(string repository, CommitSha commit, string path, CancellationToken cancellationToken);

    Task<Result<string, WorkspaceFailure>> BlobContentAsync(string repository, string blob, CancellationToken cancellationToken);

    Task<Result<Option<string>, WorkspaceFailure>> WorktreeBlobAsync(WorkspaceLocation location, string path, CancellationToken cancellationToken);
}
