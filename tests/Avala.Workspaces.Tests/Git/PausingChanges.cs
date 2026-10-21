using Avala.Sdk;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Tests.Git;

internal sealed class PausingChanges(IGitChanges inner) : IGitChanges
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;

    public Task Entered => entered.Task;

    public void Release() => released.TrySetResult();

    public async Task<Result<IReadOnlyList<FileChange>, WorkspaceFailure>> ChangedFilesAsync(string repository, CommitSha from, CommitSha to, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref calls) == 1)
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }

        return await inner.ChangedFilesAsync(repository, from, to, cancellationToken);
    }

    public Task<Result<Option<CommitSha>, WorkspaceFailure>> TipOfAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        inner.TipOfAsync(repository, branch, cancellationToken);

    public Task<Result<string, WorkspaceFailure>> TreeOfAsync(string repository, CommitSha commit, CancellationToken cancellationToken) =>
        inner.TreeOfAsync(repository, commit, cancellationToken);

    public Task<Result<Option<FileDiff>, WorkspaceFailure>> FileDiffAsync(string repository, CommitSha from, CommitSha to, string path, CancellationToken cancellationToken) =>
        inner.FileDiffAsync(repository, from, to, path, cancellationToken);

    public Task<Result<MergedTree, WorkspaceFailure>> MergeTreeAsync(string repository, CommitSha mergeBase, CommitSha target, CommitSha work, CancellationToken cancellationToken) =>
        inner.MergeTreeAsync(repository, mergeBase, target, work, cancellationToken);

    public Task<Result<Option<string>, WorkspaceFailure>> CheckoutOfAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        inner.CheckoutOfAsync(repository, branch, cancellationToken);

    public Task<Result<bool, WorkspaceFailure>> IsCleanAsync(string checkout, CancellationToken cancellationToken) =>
        inner.IsCleanAsync(checkout, cancellationToken);

    public Task<Result<CommitSha, WorkspaceFailure>> CommitTreeAsync(string repository, string tree, CommitSha parent, string message, CancellationToken cancellationToken) =>
        inner.CommitTreeAsync(repository, tree, parent, message, cancellationToken);

    public Task<bool> MoveBranchAsync(string repository, BranchName branch, CommitSha to, CommitSha from, CancellationToken cancellationToken) =>
        inner.MoveBranchAsync(repository, branch, to, from, cancellationToken);

    public Task<bool> AdvanceCheckoutAsync(string checkout, CommitSha from, CommitSha to, CancellationToken cancellationToken) =>
        inner.AdvanceCheckoutAsync(checkout, from, to, cancellationToken);

    public Task<Result<Option<CommitSha>, WorkspaceFailure>> LastMergedAsync(string repository, WorkspaceId workspace, CancellationToken cancellationToken) =>
        inner.LastMergedAsync(repository, workspace, cancellationToken);

    public Task<bool> RecordMergedAsync(string repository, WorkspaceId workspace, CommitSha work, CancellationToken cancellationToken) =>
        inner.RecordMergedAsync(repository, workspace, work, cancellationToken);
}
