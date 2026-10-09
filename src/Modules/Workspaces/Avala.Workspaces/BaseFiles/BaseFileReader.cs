using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.BaseFiles;

internal sealed class BaseFileReader(IGit git, IWorkspaceStore store) : IBaseFiles
{
    private const string Head = "HEAD";

    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken) =>
        await (await store.FindAtAsync(worktree, cancellationToken))
            .ToResult(WorkspaceFailure.UnknownWorkspace)
            .BindAsync(workspace => ReadAsync(workspace.Location, workspace.Base, path, cancellationToken));

    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadCurrentAsync(string repository, string path, CancellationToken cancellationToken)
    {
        if (!(await git.FindRepositoryRootAsync(repository, cancellationToken)).TryGetValue(out var root, out var failure)
            || !(await git.ResolveCommitAsync(root, Head, cancellationToken)).TryGetValue(out var commit, out failure)
            || !WorkspaceLocation.Create(root, root).TryGetValue(out var checkout, out _))
        {
            return failure;
        }

        return await ReadAsync(checkout, commit, path, cancellationToken);
    }

    private async Task<Result<BaseFile, WorkspaceFailure>> ReadAsync(
        WorkspaceLocation location,
        CommitSha commit,
        string path,
        CancellationToken cancellationToken)
    {
        if (!(await git.CommittedBlobAsync(location.Repository, commit, path, cancellationToken)).TryGetValue(out var committed, out var failure)
            || !(await ContentAsync(location.Repository, committed, cancellationToken)).TryGetValue(out var content, out failure)
            || !(await git.WorktreeBlobAsync(location, path, cancellationToken)).TryGetValue(out var current, out failure))
        {
            return failure;
        }

        return new BaseFile(path, new FileOrigin(commit.Value, current != committed), content);
    }

    private async Task<Result<Option<string>, WorkspaceFailure>> ContentAsync(
        string repository,
        Option<string> blob,
        CancellationToken cancellationToken) =>
        await blob.Match(
            found => git.BlobContentAsync(repository, found, cancellationToken).MapAsync(Option<string>.Some),
            () => Task.FromResult(Result<Option<string>, WorkspaceFailure>.Success(Option<string>.None)));
}
