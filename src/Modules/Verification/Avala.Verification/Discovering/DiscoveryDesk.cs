using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class DiscoveryDesk(DiscoveryBook book, DiscoveredJobs jobs, ProposalReview review, JobActions actions) : IChecksProposals, IAsyncDisposable
{
    private static readonly JobStatus[] Ended = [JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    private readonly SerialExecutor owner = new();
    private ImmutableHashSet<JobId> settling = [];

    public async Task StartAsync(JobId job, CancellationToken cancellationToken) =>
        await FollowUpAsync(
            await owner.RunAsync(
                token => book.Of(job).IsSome || book.IsScout(job) ? Task.FromResult(Option<Discovery>.None) : UncheckedAsync(job, token),
                cancellationToken),
            cancellationToken);

    public async Task ScoutMovedAsync(Discovery discovery, JobStatus status, CancellationToken cancellationToken)
    {
        if (discovery.State == DiscoveryState.Proposed || (status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp) && !Ended.Contains(status)))
        {
            return;
        }

        if (!await ClaimAsync(discovery.Job, cancellationToken))
        {
            return;
        }

        try
        {
            if (status == JobStatus.AwaitingReview && discovery.Reads && ProposalReview.Proposed(discovery.Proposal).Count > 0)
            {
                await book.KeepAsync(discovery with { State = DiscoveryState.Proposed, Declaration = CheckDeclaration.Write(ProposalReview.Proposed(discovery.Proposal)) }, cancellationToken);

                return;
            }

            var confirmed = status == JobStatus.AwaitingReview
                ? await discovery.Scout.Match(scout => review.ConfirmedAsync(scout, discovery.Proposal, cancellationToken), () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]))
                : [];

            await SettleAsync(discovery, confirmed, cancellationToken);
        }
        finally
        {
            Release(discovery.Job);
        }

        if (!Ended.Contains(status))
        {
            await DiscardScoutAsync(discovery, cancellationToken);
        }
    }

    public async ValueTask AcceptAsync(JobId job, CancellationToken cancellationToken) =>
        await DecideAsync(job, accepted: true, cancellationToken);

    public async ValueTask DeclineAsync(JobId job, CancellationToken cancellationToken) =>
        await DecideAsync(job, accepted: false, cancellationToken);

    private async Task DecideAsync(JobId job, bool accepted, CancellationToken cancellationToken) =>
        await book.Of(job).Bind(found => found.State == DiscoveryState.Proposed ? found : Option<Discovery>.None).Match(
            discovery => DecideAsync(discovery, accepted, cancellationToken),
            () => Task.CompletedTask);

    private async Task DecideAsync(Discovery discovery, bool accepted, CancellationToken cancellationToken)
    {
        var job = discovery.Job;

        if (!await ClaimAsync(job, cancellationToken))
        {
            return;
        }

        try
        {
            var confirmed = accepted
                ? await discovery.Scout.Match(scout => review.ConfirmedAsync(scout, discovery.Declaration, cancellationToken), () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]))
                : [];

            await SettleAsync(discovery, confirmed, cancellationToken);
        }
        finally
        {
            Release(job);
        }

        await DiscardScoutAsync(discovery, cancellationToken);
    }

    public async Task AbandonAsync(Discovery discovery, CancellationToken cancellationToken)
    {
        if (!await ClaimAsync(discovery.Job, cancellationToken))
        {
            return;
        }

        try
        {
            await ConcludeAsync(discovery, [], cancellationToken);
        }
        finally
        {
            Release(discovery.Job);
        }

        await DiscardScoutAsync(discovery, cancellationToken);
        await HandOverAsync(discovery.Job, cancellationToken);
    }

    public async Task CatchUpAsync(Discovery follower, CancellationToken cancellationToken) =>
        await follower.Follows.Bind(book.Of).Match(
            async followed =>
            {
                if (followed.Settled && await ClaimAsync(follower.Job, cancellationToken))
                {
                    await ConcludedAsync(follower, followed.Checks, cancellationToken);
                }
            },
            () => FollowUpAsync(owner.RunAsync(token => RelaunchAsync(follower, [], token), cancellationToken), cancellationToken));

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task FollowUpAsync(Task<Option<Discovery>> launching, CancellationToken cancellationToken) =>
        await FollowUpAsync(await launching, cancellationToken);

    private async Task FollowUpAsync(Option<Discovery> launched, CancellationToken cancellationToken) =>
        await launched.Bind(discovery => discovery.Scout.Map(scout => (discovery, scout))).Match(
            async found => await ScoutMovedAsync(found.discovery, await jobs.StatusAsync(found.scout, cancellationToken), cancellationToken),
            () => Task.CompletedTask);

    private Task<bool> ClaimAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(
            _ => Task.FromResult(book.Of(job).Match(discovery => !discovery.Settled, () => false) && ImmutableInterlocked.Update(ref settling, claimed => claimed.Add(job))),
            cancellationToken);

    private void Release(JobId job) => ImmutableInterlocked.Update(ref settling, claimed => claimed.Remove(job));

    private async Task SettleAsync(Discovery discovery, IReadOnlyList<DeclaredCheck> checks, CancellationToken cancellationToken)
    {
        await ConcludeAsync(discovery, checks, cancellationToken);

        foreach (var follower in book.Followers(discovery.Job))
        {
            if (await ClaimAsync(follower.Job, cancellationToken))
            {
                await ConcludedAsync(follower, checks, cancellationToken);
            }
        }
    }

    private async Task ConcludedAsync(Discovery claimed, IReadOnlyList<DeclaredCheck> checks, CancellationToken cancellationToken)
    {
        try
        {
            await ConcludeAsync(claimed, checks, cancellationToken);
        }
        finally
        {
            Release(claimed.Job);
        }
    }

    private async Task ConcludeAsync(Discovery discovery, IReadOnlyList<DeclaredCheck> checks, CancellationToken cancellationToken)
    {
        if (checks.Count == 0)
        {
            await book.KeepAsync(discovery with { State = DiscoveryState.NoneFound }, cancellationToken);
        }
        else
        {
            var declaration = CheckDeclaration.Write(checks);
            await jobs.ShipAsync(discovery.Job, declaration, cancellationToken);
            await book.KeepAsync(discovery with { State = DiscoveryState.Found, Declaration = declaration }, cancellationToken);
        }

        await actions.RecheckAsync(discovery.Job, cancellationToken);

        foreach (var child in await jobs.ChildrenAsync(discovery.Job, cancellationToken))
        {
            await actions.RecheckAsync(child, cancellationToken);
        }
    }

    private async Task DiscardScoutAsync(Discovery discovery, CancellationToken cancellationToken) =>
        await discovery.Scout.Match(scout => actions.DiscardAsync(scout, cancellationToken), () => Task.CompletedTask);

    private async Task HandOverAsync(JobId ended, CancellationToken cancellationToken)
    {
        var followers = book.Followers(ended);

        if (followers.Count > 0)
        {
            await FollowUpAsync(owner.RunAsync(token => RelaunchAsync(followers[0], [.. followers.Skip(1)], token), cancellationToken), cancellationToken);
        }
    }

    private async Task<Option<Discovery>> RelaunchAsync(Discovery follower, IReadOnlyList<Discovery> others, CancellationToken cancellationToken)
    {
        foreach (var other in others)
        {
            await book.KeepAsync(other with { Follows = follower.Job }, cancellationToken);
        }

        return await (await jobs.PlaceOfAsync(follower, cancellationToken)).Match(
            async place => await LaunchAsync(place, follower with { Follows = Option<JobId>.None, Reads = !await review.RunsAsync(place.Repository, cancellationToken) }, cancellationToken),
            async () =>
            {
                await ConcludeAsync(follower, [], cancellationToken);

                return Option<Discovery>.None;
            });
    }

    private async Task<Option<Discovery>> UncheckedAsync(JobId job, CancellationToken cancellationToken) =>
        await (await jobs.UncheckedAsync(job, cancellationToken)).Match(
            place => DiscoverAsync(place, cancellationToken),
            () => Task.FromResult(Option<Discovery>.None));

    private async Task<Option<Discovery>> DiscoverAsync(JobPlace place, CancellationToken cancellationToken)
    {
        if (!await review.DiscoversAsync(cancellationToken))
        {
            return Option<Discovery>.None;
        }

        var running = new Discovery(place.Job, place.Repository, DiscoveryState.Running, book.Now) { Reads = !await review.RunsAsync(place.Repository, cancellationToken) };

        return await book.LatestFound(place.Repository).Match(
            async found =>
            {
                await ConcludeAsync(running, found.Checks, cancellationToken);

                return Option<Discovery>.None;
            },
            () => book.Scouting(place.Repository).Match(
                async scouting =>
                {
                    await book.KeepAsync(running with { Follows = scouting.Job }, cancellationToken);

                    return Option<Discovery>.None;
                },
                () => LaunchAsync(place, running, cancellationToken)));
    }

    private async Task<Option<Discovery>> LaunchAsync(JobPlace place, Discovery running, CancellationToken cancellationToken) =>
        await (await actions.LaunchAsync(place, !running.Reads, cancellationToken)).Match(
            async scout =>
            {
                var launched = running with { Scout = scout };
                await book.KeepAsync(launched, cancellationToken);

                return Option<Discovery>.Some(launched);
            },
            async _ =>
            {
                await ConcludeAsync(running, [], cancellationToken);

                return Option<Discovery>.None;
            });
}
