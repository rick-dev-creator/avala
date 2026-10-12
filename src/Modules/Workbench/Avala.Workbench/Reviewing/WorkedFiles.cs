using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed class WorkedFiles(IJobCatalog catalog, IWorkspaceChanges changes, IWorkspaces workspaces, IWorkingFiles files)
{
    public async Task<Result<FileDiff, WorkspaceFailure>> ChangesAsync(JobId job, string path, CancellationToken cancellationToken) =>
        await (await catalog.WorkspaceOfAsync(job, cancellationToken)).Match(
            async workspace => await changes.FileDiffAsync(workspace, path, cancellationToken),
            () => Task.FromResult(Result<FileDiff, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

    public Task<Result<Option<string>, WorkspaceFailure>> LocateAsync(JobId job, string path, CancellationToken cancellationToken) =>
        FirstAsync(job, folder => files.LocateAsync(folder, path, cancellationToken), cancellationToken);

    public Task<Result<Option<byte[]>, WorkspaceFailure>> ReadAsync(JobId job, string path, int limit, CancellationToken cancellationToken) =>
        FirstAsync(job, folder => files.ReadBytesAsync(folder, path, limit, cancellationToken), cancellationToken);

    private async Task<Result<Option<T>, WorkspaceFailure>> FirstAsync<T>(JobId job, Func<string, ValueTask<Result<Option<T>, WorkspaceFailure>>> read, CancellationToken cancellationToken)
        where T : notnull
    {
        var found = Result<Option<T>, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace);

        foreach (var folder in await FoldersAsync(job, cancellationToken))
        {
            found = await read(folder);

            if (found.Match(file => file.IsSome, _ => false))
            {
                return found;
            }
        }

        return found;
    }

    private async Task<IReadOnlyList<string>> FoldersAsync(JobId job, CancellationToken cancellationToken)
    {
        var history = await catalog.HistoryAsync(job, cancellationToken);

        return await history.Match(
            async found => await found.Summary.Workspace.Match(
                async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match<IReadOnlyList<string>>(
                    info => [info.Path, found.Summary.Repository],
                    _ => [found.Summary.Repository]),
                () => Task.FromResult<IReadOnlyList<string>>([found.Summary.Repository])),
            () => Task.FromResult<IReadOnlyList<string>>([]));
    }
}
