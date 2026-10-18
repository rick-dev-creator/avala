using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Readying;
using Avala.Workbench.Steering;
using Avala.Workbench.Submitting;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Reviewing;

internal sealed record ApprovalAttempt(Result<ApprovalDelivery, JobRejection> Outcome, IReadOnlyList<string> Conflicts)
{
    public Option<ForgeError> ForgeRefusal { get; init; }
}

internal sealed record DeliveryFacts(Option<DeliveryPlan> Plan, Option<PullRequestOffer> PullRequest, Option<ForgeReadiness> Forge)
{
    public bool Documented { get; init; }
}

internal sealed class ForgeGaps(IForgeReadiness readiness, IReadinessLinks links)
{
    public async Task<Option<ForgeReadiness>> OfAsync(string worktree, CancellationToken cancellationToken) =>
        Option<ForgeReadiness>.Some(await readiness.OfAsync(worktree, cancellationToken));

    public void OpenSettings() => links.OpenSettings();
}

internal sealed class DeliveryOffers(IDeliveryPlans plans, ForgeDesk forge, ForgeGaps gaps, JobBoard board)
{
    public ForgeDesk Forge => forge;

    public async Task<DeliveryFacts> OfAsync(JobId job, CancellationToken cancellationToken)
    {
        var plan = (await plans.PlanAsync(job, cancellationToken)).Match(Option<DeliveryPlan>.Some, _ => Option<DeliveryPlan>.None);
        var offer = await forge.OfferAsync(job, cancellationToken);
        var gap = offer.IsSome || plan.Match(found => found.IntoParent, () => true)
            ? Option<ForgeReadiness>.None
            : await plan.Match(
                found => gaps.OfAsync(found.Worktree, cancellationToken),
                () => Task.FromResult(Option<ForgeReadiness>.None));

        return new DeliveryFacts(plan, offer, gap) { Documented = board.Docs.ContainsKey(job) };
    }

    public void OpenSettings() => gaps.OpenSettings();
}

internal sealed class ReviewDesk(IJobs jobs, ConflictFinder conflicts, QueuedMessages queue, DeliveryOffers offers)
{
    private ForgeDesk Forge => offers.Forge;

    public Option<string> Queued(JobId job) => queue.Find(job);

    public async Task<Result<JobId, JobRejection>> SendBackQueuedAsync(JobId job, CancellationToken cancellationToken) =>
        (await queue.SendBackAsync(job, cancellationToken)).Map(continued => continued.Job);

    public async Task<ApprovalAttempt> ApproveAsync(JobId job, CancellationToken cancellationToken)
    {
        var outcome = (await jobs.ApproveAsync(job, cancellationToken)).Map(approval => approval.Delivery);
        var conflicted = outcome.Match(_ => false, rejection => rejection == JobRejection.MergeConflict);

        return new ApprovalAttempt(outcome, conflicted ? await conflicts.OfAsync(job, cancellationToken) : [])
        {
            ForgeRefusal = outcome.IsSuccess ? Option<ForgeError>.None : Forge.RefusalOf(job),
        };
    }

    public Task<DeliveryFacts> OfferAsync(JobId job, CancellationToken cancellationToken) => offers.OfAsync(job, cancellationToken);

    public void OpenSettings() => offers.OpenSettings();

    public async Task<ApprovalAttempt> OpenPullRequestAsync(JobId job, CancellationToken cancellationToken)
    {
        var outcome = await Forge.OpenAsync(job, cancellationToken);

        return new ApprovalAttempt(outcome, []) { ForgeRefusal = outcome.IsSuccess ? Option<ForgeError>.None : Forge.RefusalOf(job) };
    }

    public async Task<Result<JobId, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        (await jobs.SendBackAsync(job, feedback, cancellationToken)).Map(continued => continued.Job);

    public async Task<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        await jobs.DiscardAsync(job, cancellationToken);

    public async Task<Result<JobId, JobRejection>> ResubmitAsync(JobSummary held, Option<ConnectionChoice> choice, CancellationToken cancellationToken)
    {
        var submitted = await jobs.SubmitAsync(Resubmission.Of(held, choice), cancellationToken);

        if (submitted.IsSuccess)
        {
            _ = await jobs.DiscardAsync(held.Job, cancellationToken);
        }

        return submitted;
    }
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
