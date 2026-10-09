using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed record ApprovalAttempt(Result<ApprovalDelivery, JobRejection> Outcome, IReadOnlyList<string> Conflicts);

internal sealed class ReviewDesk(IJobs jobs, IJobCatalog catalog, IWorkspaceChanges changes)
{
    public async Task<ApprovalAttempt> ApproveAsync(JobId job, CancellationToken cancellationToken)
    {
        var outcome = (await jobs.ApproveAsync(job, cancellationToken)).Map(approval => approval.Delivery);
        var conflicted = outcome.Match(_ => false, rejection => rejection == JobRejection.MergeConflict);

        return new ApprovalAttempt(outcome, conflicted ? await ConflictsAsync(job, cancellationToken) : []);
    }

    public async Task<Result<JobId, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        (await jobs.SendBackAsync(job, feedback, cancellationToken)).Map(continued => continued.Job);

    public async Task<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        await jobs.DiscardAsync(job, cancellationToken);

    private async Task<IReadOnlyList<string>> ConflictsAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.WorkspaceOfAsync(job, cancellationToken)).Match(
            async workspace => (await changes.ConflictsAsync(workspace, cancellationToken)).Match(files => files, _ => []),
            () => Task.FromResult<IReadOnlyList<string>>([]));
}
