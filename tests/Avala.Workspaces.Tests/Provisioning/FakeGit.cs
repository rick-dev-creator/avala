using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Tests.Workspaces;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Tests.Provisioning;

internal sealed class FakeGit : IGit
{
    private readonly List<WorkspaceLocation> worktrees = [];
    private int commits;

    public string Repository { get; init; } = "/repos/shop";

    public bool InsideRepository { get; init; } = true;

    public bool BranchTaken { get; init; }

    public bool WorktreeFails { get; init; }

    public IReadOnlyList<WorkspaceLocation> Worktrees => worktrees;

    public IReadOnlyList<BranchName> Branches { get; private set; } = [];

    public Task<Result<string, WorkspaceFailure>> FindRepositoryRootAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string, WorkspaceFailure>>(InsideRepository ? Repository : WorkspaceFailure.NotAGitRepository);

    public CommitSha Head { get; init; } = Given.Commit(0);

    public IReadOnlyList<CommitSha> CheckedOut { get; private set; } = [];

    public Task<Result<CommitSha, WorkspaceFailure>> ResolveCommitAsync(string repository, string reference, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CommitSha, WorkspaceFailure>>(Head);

    public Task<Result<Option<string>, WorkspaceFailure>> CommittedBlobAsync(
        string repository,
        CommitSha commit,
        string path,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<Option<string>, WorkspaceFailure>>(Option<string>.None);

    public Task<Result<string, WorkspaceFailure>> BlobContentAsync(string repository, string blob, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string, WorkspaceFailure>>(WorkspaceFailure.GitFailed);

    public Task<Result<Option<string>, WorkspaceFailure>> WorktreeBlobAsync(
        WorkspaceLocation location,
        string path,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<Option<string>, WorkspaceFailure>>(Option<string>.None);

    public Task<Result<bool, WorkspaceFailure>> BranchExistsAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        Task.FromResult<Result<bool, WorkspaceFailure>>(BranchTaken);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> AddWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        CommitSha commit,
        CancellationToken cancellationToken)
    {
        if (WorktreeFails)
        {
            return Task.FromResult<Result<WorkspaceLocation, WorkspaceFailure>>(WorkspaceFailure.GitFailed);
        }

        worktrees.Add(location);
        CheckedOut = [.. CheckedOut, commit];
        Branches = [.. Branches, branch];

        return Task.FromResult<Result<WorkspaceLocation, WorkspaceFailure>>(location);
    }

    public Task<Result<CommitSha, WorkspaceFailure>> CommitAllAsync(
        WorkspaceLocation location,
        string label,
        CancellationToken cancellationToken) =>
        Task.FromResult<Result<CommitSha, WorkspaceFailure>>(Given.Commit(++commits));

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        CancellationToken cancellationToken)
    {
        worktrees.Remove(location);
        Branches = [.. Branches.Where(existing => existing != branch)];

        return Task.FromResult<Result<WorkspaceLocation, WorkspaceFailure>>(location);
    }
}
