using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Watching;

internal sealed class JobPlaces(IJobCatalog catalog, IWorkspaces workspaces)
{
    public async Task<Option<JobStatus>> StatusAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Map(history => history.Summary.Status);

    public async Task<Option<WorkspaceInfo>> WorktreeAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Bind(history => history.Summary.Workspace).Match(
            async id => (await workspaces.FindAsync(id, cancellationToken)).Match(Option<WorkspaceInfo>.Some, _ => Option<WorkspaceInfo>.None),
            () => Task.FromResult(Option<WorkspaceInfo>.None));
}

internal sealed class PullRequestReader(ForgeConnections connections)
{
    public ValueTask<TimeSpan> PollAsync(CancellationToken cancellationToken) => connections.PollAsync(cancellationToken);

    public async Task<Result<PullRequestState, ForgeError>> ReadAsync(KeptWatch kept, CancellationToken cancellationToken) =>
        await (await connections.ResolveAsync(kept.State.Forge, kept.RemoteUrl, cancellationToken)).Match(
            async forge => await forge.Forge.ReadAsync(forge.Context, kept.State.PullRequest, cancellationToken),
            error => Task.FromResult(Result<PullRequestState, ForgeError>.Failure(error)));

    public async Task<Result<CommentRef, ForgeError>> CommentAsync(KeptWatch kept, string body, CancellationToken cancellationToken) =>
        await (await connections.ResolveAsync(kept.State.Forge, kept.RemoteUrl, cancellationToken)).Match(
            async forge => await forge.Forge.CommentAsync(forge.Context, kept.State.PullRequest, body, cancellationToken),
            error => Task.FromResult(Result<CommentRef, ForgeError>.Failure(error)));
}

internal sealed class Waker(IOpenDeliveries deliveries, JobPlaces places, IGitRemote git)
{
    public Task<Option<JobStatus>> StatusAsync(JobId job, CancellationToken cancellationToken) => places.StatusAsync(job, cancellationToken);

    public async Task<Result<JobContinuation, JobRejection>> WakeAsync(KeptWatch kept, Trigger trigger, string feedback, CancellationToken cancellationToken)
    {
        if (trigger.Reason == WakeReason.Conflict)
        {
            await (await places.WorktreeAsync(kept.State.Job, cancellationToken)).Match(
                async worktree => _ = await git.FetchAsync(worktree.Path, kept.Remote, kept.State.PullRequest.Base, cancellationToken),
                () => Task.CompletedTask);
        }

        return await deliveries.ReopenAsync(kept.State.Job, feedback, cancellationToken);
    }

    public async Task<Result<JobApproval, JobRejection>> RedeliverAsync(JobId job, CancellationToken cancellationToken) =>
        await deliveries.ApproveThroughAsync(job, PullRequestDelivery.Strategy, cancellationToken);
}

internal sealed class PullRequestOffers(JobPlaces places, IPullRequestRules rules, ForgeConnections connections)
{
    public async ValueTask<Option<PullRequestOffer>> OfferAsync(JobId job, CancellationToken cancellationToken) =>
        await (await places.WorktreeAsync(job, cancellationToken)).Match(
            async worktree => await (await rules.OfWorktreeAsync(worktree.Path, cancellationToken)).Match(
                async declared => await declared.Match(
                    async found => (await connections.UsableAsync(found.Forge, cancellationToken)).Match(
                        forge => Option<PullRequestOffer>.Some(new PullRequestOffer(found.Forge, forge.Info.Name, found.Remote)),
                        _ => Option<PullRequestOffer>.None),
                    () => Task.FromResult(Option<PullRequestOffer>.None)),
                _ => Task.FromResult(Option<PullRequestOffer>.None)),
            () => Task.FromResult(Option<PullRequestOffer>.None));
}

internal sealed class PullRequestDesk(WatchBook book, PullRequestWatcher watcher, PullRequestOffers offers) : IPullRequests
{
    public ValueTask<Option<PullRequestOffer>> OfferAsync(JobId job, CancellationToken cancellationToken) => offers.OfferAsync(job, cancellationToken);

    public Option<PullRequestWatchState> Of(JobId job) => book.Find(job).Map(kept => kept.State);

    public IReadOnlyList<WakeUpRecord> WakeUpsOf(JobId job) => book.WakeUpsOf(job);

    public Option<ForgeError> RefusalOf(JobId job) => book.RefusalOf(job);

    public async ValueTask<Result<PullRequestWatchState, ForgeError>> RefreshAsync(JobId job, CancellationToken cancellationToken) =>
        await watcher.RefreshAsync(job, cancellationToken);
}
