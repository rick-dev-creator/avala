using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed record ReviewFacts(JobId Job, Option<RunEvidence> Evidence, Option<UsageSummary> Usage, Result<WorkspaceDiff, WorkspaceFailure> Diff);

internal sealed class ReviewReader(IRunEvidence evidence, IJobCatalog catalog, IWorkspaceChanges changes, IUsage usage)
{
    public async Task<ReviewFacts> ReadAsync(JobId job, CancellationToken cancellationToken)
    {
        var workspace = await catalog.WorkspaceOfAsync(job, cancellationToken);
        var diff = await workspace.Match(
            async found => await changes.DiffAsync(found, cancellationToken),
            () => Task.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

        return new ReviewFacts(job, await evidence.OfJobAsync(job, cancellationToken), usage.OfJob(job), diff);
    }

    public async Task<Result<FileDiff, WorkspaceFailure>> HunksAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
        await changes.FileDiffAsync(workspace, path, cancellationToken);
}

internal static class CatalogQueries
{
    extension(IJobCatalog catalog)
    {
        public async Task<Option<WorkspaceId>> WorkspaceOfAsync(JobId job, CancellationToken cancellationToken) =>
            (await catalog.HistoryAsync(job, cancellationToken)).Bind(history => history.Summary.Workspace);
    }
}
