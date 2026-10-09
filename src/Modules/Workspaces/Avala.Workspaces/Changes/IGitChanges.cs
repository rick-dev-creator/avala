using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Changes;

internal sealed record MergedTree(string Tree, IReadOnlyList<string> Conflicts);

internal interface IGitChanges
{
    Task<Result<Option<CommitSha>, WorkspaceFailure>> TipOfAsync(string repository, BranchName branch, CancellationToken cancellationToken);

    Task<Result<string, WorkspaceFailure>> TreeOfAsync(string repository, CommitSha commit, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<FileChange>, WorkspaceFailure>> ChangedFilesAsync(string repository, CommitSha from, CommitSha to, CancellationToken cancellationToken);

    Task<Result<Option<FileDiff>, WorkspaceFailure>> FileDiffAsync(
        string repository,
        CommitSha from,
        CommitSha to,
        string path,
        CancellationToken cancellationToken);

    Task<Result<MergedTree, WorkspaceFailure>> MergeTreeAsync(
        string repository,
        CommitSha mergeBase,
        CommitSha target,
        CommitSha work,
        CancellationToken cancellationToken);

    Task<Result<Option<string>, WorkspaceFailure>> CheckoutOfAsync(string repository, BranchName branch, CancellationToken cancellationToken);

    Task<Result<bool, WorkspaceFailure>> IsCleanAsync(string checkout, CancellationToken cancellationToken);

    Task<Result<CommitSha, WorkspaceFailure>> CommitTreeAsync(
        string repository,
        string tree,
        CommitSha parent,
        string message,
        CancellationToken cancellationToken);

    Task<bool> MoveBranchAsync(string repository, BranchName branch, CommitSha to, CommitSha from, CancellationToken cancellationToken);

    Task<bool> AdvanceCheckoutAsync(string checkout, CommitSha from, CommitSha to, CancellationToken cancellationToken);
}
