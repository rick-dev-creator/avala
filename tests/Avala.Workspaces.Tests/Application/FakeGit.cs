using Avala.Sdk;
using Avala.Workspaces.Application;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;
using Avala.Workspaces.Tests.Domain;

namespace Avala.Workspaces.Tests.Application;

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

    public Task<Result<bool, WorkspaceFailure>> BranchExistsAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        Task.FromResult<Result<bool, WorkspaceFailure>>(BranchTaken);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> AddWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        string baseRef,
        CancellationToken cancellationToken)
    {
        if (WorktreeFails)
        {
            return Task.FromResult<Result<WorkspaceLocation, WorkspaceFailure>>(WorkspaceFailure.GitFailed);
        }

        worktrees.Add(location);
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
