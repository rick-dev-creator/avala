using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Steering;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed record ApprovalAttempt(Result<ApprovalDelivery, JobRejection> Outcome, IReadOnlyList<string> Conflicts)
{
    public Option<ForgeError> ForgeRefusal { get; init; }
}

internal sealed class ReviewDesk(IJobs jobs, ConflictFinder conflicts, QueuedMessages queue, ForgeDesk forge)
{
    public Option<string> Queued(JobId job) => queue.Find(job);

    public async Task<Result<JobId, JobRejection>> SendBackQueuedAsync(JobId job, CancellationToken cancellationToken) =>
        (await queue.SendBackAsync(job, cancellationToken)).Map(continued => continued.Job);

    public async Task<ApprovalAttempt> ApproveAsync(JobId job, CancellationToken cancellationToken)
    {
        var outcome = (await jobs.ApproveAsync(job, cancellationToken)).Map(approval => approval.Delivery);
        var conflicted = outcome.Match(_ => false, rejection => rejection == JobRejection.MergeConflict);

        return new ApprovalAttempt(outcome, conflicted ? await conflicts.OfAsync(job, cancellationToken) : [])
        {
            ForgeRefusal = outcome.IsSuccess ? Option<ForgeError>.None : forge.RefusalOf(job),
        };
    }

    public ValueTask<Option<PullRequestOffer>> PullRequestOfferAsync(JobId job, CancellationToken cancellationToken) => forge.OfferAsync(job, cancellationToken);

    public async Task<ApprovalAttempt> OpenPullRequestAsync(JobId job, CancellationToken cancellationToken)
    {
        var outcome = await forge.OpenAsync(job, cancellationToken);

        return new ApprovalAttempt(outcome, []) { ForgeRefusal = outcome.IsSuccess ? Option<ForgeError>.None : forge.RefusalOf(job) };
    }

    public async Task<Result<JobId, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        (await jobs.SendBackAsync(job, feedback, cancellationToken)).Map(continued => continued.Job);

    public async Task<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        await jobs.DiscardAsync(job, cancellationToken);
}

internal sealed class ConflictFinder(IJobCatalog catalog, IWorkspaceChanges changes)
{
    public async Task<IReadOnlyList<string>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.WorkspaceOfAsync(job, cancellationToken)).Match(
            async workspace => (await changes.ConflictsAsync(workspace, cancellationToken)).Match(files => files, _ => []),
            () => Task.FromResult<IReadOnlyList<string>>([]));
}

internal sealed class ForgeDesk(IPullRequests pullRequests, IOpenDeliveries deliveries)
{
    public ValueTask<Option<PullRequestOffer>> OfferAsync(JobId job, CancellationToken cancellationToken) => pullRequests.OfferAsync(job, cancellationToken);

    public async Task<Result<ApprovalDelivery, JobRejection>> OpenAsync(JobId job, CancellationToken cancellationToken) =>
        (await deliveries.ApproveThroughAsync(job, PullRequestDelivery.Strategy, cancellationToken)).Map(approval => approval.Delivery);

    public Option<ForgeError> RefusalOf(JobId job) => pullRequests.RefusalOf(job);

    public async Task<Result<PullRequestWatchState, ForgeError>> RefreshAsync(JobId job, CancellationToken cancellationToken) =>
        await pullRequests.RefreshAsync(job, cancellationToken);
}
