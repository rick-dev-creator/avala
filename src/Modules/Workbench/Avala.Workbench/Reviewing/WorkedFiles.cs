using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed class WorkedFiles(IJobCatalog catalog, IWorkspaceChanges changes)
{
    public async Task<Result<FileDiff, WorkspaceFailure>> ChangesAsync(JobId job, string path, CancellationToken cancellationToken) =>
        await (await catalog.WorkspaceOfAsync(job, cancellationToken)).Match(
            async workspace => await changes.FileDiffAsync(workspace, path, cancellationToken),
            () => Task.FromResult(Result<FileDiff, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));
}
