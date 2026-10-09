using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.BaseFiles;

internal sealed class BaseFileReader(IGit git, IWorkspaceStore store) : IBaseFiles
{
    public async ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken) =>
        await (await store.FindAtAsync(worktree, cancellationToken))
            .ToResult(WorkspaceFailure.UnknownWorkspace)
            .BindAsync(workspace => ReadAsync(workspace, path, cancellationToken));

    private async Task<Result<BaseFile, WorkspaceFailure>> ReadAsync(Workspace workspace, string path, CancellationToken cancellationToken)
    {
        if (!(await git.CommittedBlobAsync(workspace.Location.Repository, workspace.Base, path, cancellationToken)).TryGetValue(out var committed, out var failure)
            || !(await ContentAsync(workspace, committed, cancellationToken)).TryGetValue(out var content, out failure)
            || !(await git.WorktreeBlobAsync(workspace.Location, path, cancellationToken)).TryGetValue(out var current, out failure))
        {
            return failure;
        }

        return new BaseFile(path, new FileOrigin(workspace.Base.Value, current != committed), content);
    }

    private async Task<Result<Option<string>, WorkspaceFailure>> ContentAsync(
        Workspace workspace,
        Option<string> blob,
        CancellationToken cancellationToken) =>
        await blob.Match(
            found => git.BlobContentAsync(workspace.Location.Repository, found, cancellationToken).MapAsync(Option<string>.Some),
            () => Task.FromResult(Result<Option<string>, WorkspaceFailure>.Success(Option<string>.None)));
}
