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
            await (status == JobStatus.AwaitingReview ? ReviewAsync(discovery, cancellationToken) : SettleAsync(discovery, [], final: false, cancellationToken));
        }
        finally
        {
            Release(discovery.Job);
        }

        if (!Ended.Contains(status) && book.Of(discovery.Job).Match(found => found.Settled, () => true))
        {
            await DiscardScoutAsync(discovery, cancellationToken);
        }
    }

    public async ValueTask AcceptAsync(JobId job, CancellationToken cancellationToken) =>
        await DecideAsync(job, accepted: true, cancellationToken);

    public async ValueTask DeclineAsync(JobId job, CancellationToken cancellationToken) =>
        await DecideAsync(job, accepted: false, cancellationToken);

    public async Task AbandonAsync(Discovery discovery, CancellationToken cancellationToken)
    {
        if (!await ClaimAsync(discovery.Job, cancellationToken))
        {
            return;
        }

        try
        {
            await ConcludeAsync(discovery, [], final: false, cancellationToken);
        }
        finally
        {
            Release(discovery.Job);
        }

        await DiscardScoutAsync(discovery, cancellationToken);
        await HandOverAsync(discovery, cancellationToken);
    }

    public async Task CatchUpAsync(Discovery follower, CancellationToken cancellationToken) =>
        await follower.Follows.Bind(book.Of).Match(
            async followed =>
            {
                if (followed.Settled && await ClaimAsync(follower.Job, cancellationToken))
                {
                    await ConcludedAsync(follower, followed.Checks, followed.Final, cancellationToken);
                }
            },
            () => FollowUpAsync(owner.RunAsync(token => RelaunchAsync(follower, [], token), cancellationToken), cancellationToken));

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task ReviewAsync(Discovery discovery, CancellationToken cancellationToken)
    {
        var proposed = ProposalReview.Proposed(discovery.Proposal);

        if (discovery.Reads && proposed.Count > 0)
        {
            await book.KeepAsync(discovery with { State = DiscoveryState.Proposed, Declaration = CheckDeclaration.Write(proposed) }, cancellationToken);

            foreach (var follower in book.Followers(discovery.Job))
            {
                await book.AnnounceAsync(follower.Job, cancellationToken);
            }

            return;
        }

        var confirmed = await discovery.Scout.Match(
            scout => review.ConfirmedAsync(scout, ProposalReview.RunBefore(proposed, discovery.Ran), cancellationToken),
            () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]));

        await SettleAsync(discovery, confirmed, final: true, cancellationToken);
    }

    private async Task DecideAsync(JobId job, bool accepted, CancellationToken cancellationToken) =>
        await book.Of(job).Bind(found => found.State == DiscoveryState.Proposed ? found : book.Followed(found)).Match(
            discovery => DecideAsync(discovery, accepted, cancellationToken),
            () => Task.CompletedTask);

    private async Task DecideAsync(Discovery discovery, bool accepted, CancellationToken cancellationToken)
    {
        if (!await ClaimAsync(discovery.Job, cancellationToken))
        {
            return;
        }

        try
        {
            var chosen = accepted ? await review.ChosenAsync(discovery, cancellationToken) : [];
            await SettleAsync(discovery, chosen, final: true, cancellationToken);
        }
        finally
        {
            Release(discovery.Job);
        }

        await DiscardScoutAsync(discovery, cancellationToken);
    }

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

    private async Task SettleAsync(Discovery discovery, IReadOnlyList<DeclaredCheck> checks, bool final, CancellationToken cancellationToken)
    {
        await ConcludeAsync(discovery, checks, final, cancellationToken);

        foreach (var follower in book.Followers(discovery.Job))
        {
            if (await ClaimAsync(follower.Job, cancellationToken))
            {
                await ConcludedAsync(follower, checks, final, cancellationToken);
            }
        }
    }

    private async Task ConcludedAsync(Discovery claimed, IReadOnlyList<DeclaredCheck> checks, bool final, CancellationToken cancellationToken)
    {
        try
        {
            await ConcludeAsync(claimed, checks, final, cancellationToken);
        }
        finally
        {
            Release(claimed.Job);
        }
    }

    private async Task ConcludeAsync(Discovery discovery, IReadOnlyList<DeclaredCheck> checks, bool final, CancellationToken cancellationToken)
    {
        if (checks.Count == 0)
        {
            await book.KeepAsync(discovery with { State = DiscoveryState.NoneFound, Declaration = string.Empty, Final = final }, cancellationToken);
        }
        else
        {
            var declaration = CheckDeclaration.Write(checks);
            await jobs.ShipAsync(discovery.Job, declaration, cancellationToken);
            await book.KeepAsync(discovery with { State = DiscoveryState.Found, Declaration = declaration, Final = true }, cancellationToken);
        }

        await RecheckAsync(discovery.Job, cancellationToken);
    }

    private async Task RecheckAsync(JobId job, CancellationToken cancellationToken)
    {
        await actions.RecheckAsync(job, cancellationToken);

        foreach (var child in await jobs.ChildrenAsync(job, cancellationToken))
        {
            await RecheckAsync(child, cancellationToken);
        }
    }

    private async Task DiscardScoutAsync(Discovery discovery, CancellationToken cancellationToken) =>
        await discovery.Scout.Match(scout => actions.DiscardAsync(scout, cancellationToken), () => Task.CompletedTask);

    private async Task HandOverAsync(Discovery ended, CancellationToken cancellationToken)
    {
        var followers = book.Followers(ended.Job);

        if (followers.Count == 0)
        {
            return;
        }

        if (ended.State == DiscoveryState.Proposed)
        {
            await InheritAsync(ended, followers, cancellationToken);

            return;
        }

        await FollowUpAsync(owner.RunAsync(token => RelaunchAsync(followers[0], [.. followers.Skip(1)], token), cancellationToken), cancellationToken);
    }

    private async Task InheritAsync(Discovery proposal, IReadOnlyList<Discovery> followers, CancellationToken cancellationToken)
    {
        var heir = followers[0];
        await book.KeepAsync(heir with { State = DiscoveryState.Proposed, Declaration = proposal.Declaration, Reads = proposal.Reads, Follows = Option<JobId>.None }, cancellationToken);

        foreach (var other in followers.Skip(1))
        {
            await book.KeepAsync(other with { Follows = heir.Job }, cancellationToken);
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
                await ConcludeAsync(follower, [], final: false, cancellationToken);

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

        return await book.LatestDecided(place.Repository).Match(
            async decided =>
            {
                await ConcludeAsync(running, decided.Checks, final: true, cancellationToken);

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
                await ConcludeAsync(running, [], final: false, cancellationToken);

                return Option<Discovery>.None;
            });
}
