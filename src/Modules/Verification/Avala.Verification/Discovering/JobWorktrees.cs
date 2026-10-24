using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class JobWorktrees(IJobCatalog catalog, IWorkspaces workspaces)
{
    public async Task<Option<JobSummary>> SummaryAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Map(history => history.Summary);

    public async Task<JobStatus> StatusAsync(JobId job, CancellationToken cancellationToken) =>
        (await SummaryAsync(job, cancellationToken)).Match(summary => summary.Status, () => JobStatus.Discarded);

    public async Task<Option<string>> WorktreeAsync(JobId job, CancellationToken cancellationToken) =>
        await (await SummaryAsync(job, cancellationToken)).Bind(summary => summary.Workspace).Match(
            async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match(info => Option<string>.Some(info.Path), _ => Option<string>.None),
            () => Task.FromResult(Option<string>.None));
}
