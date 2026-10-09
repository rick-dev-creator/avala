using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed record ReviewFacts(JobId Job, Option<RunEvidence> Evidence, Option<UsageSummary> Usage, Result<WorkspaceDiff, WorkspaceFailure> Diff)
{
    public Option<JobHistory> History { get; init; }
}

internal sealed class ReviewReader(IRunEvidence evidence, IJobCatalog catalog, IWorkspaceChanges changes, IUsage usage)
{
    public async Task<ReviewFacts> ReadAsync(JobId job, CancellationToken cancellationToken)
    {
        var history = await catalog.HistoryAsync(job, cancellationToken);
        var diff = await history.Bind(found => found.Summary.Workspace).Match(
            async found => await changes.DiffAsync(found, cancellationToken),
            () => Task.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

        return new ReviewFacts(job, await evidence.OfJobAsync(job, cancellationToken), usage.OfJob(job), diff) { History = history };
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
