using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class DiscoveryDesk(DiscoveryBook book, DiscoveredJobs jobs, ProposalReview review, ScoutLauncher launcher) : IAsyncDisposable
{
    private static readonly JobStatus[] Ended = [JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    private readonly SerialExecutor owner = new();
    private ImmutableHashSet<JobId> settling = [];

    public async Task StartAsync(JobId job, CancellationToken cancellationToken)
    {
        var launched = await owner.RunAsync(
            token => book.Of(job).IsSome || book.IsScout(job) ? Task.FromResult(Option<Discovery>.None) : UncheckedAsync(job, token),
            cancellationToken);

        await launched.Match(
            async discovery => await discovery.Scout.Match(
                async scout => await ScoutMovedAsync(discovery, await jobs.StatusAsync(scout, cancellationToken), cancellationToken),
                () => Task.CompletedTask),
            () => Task.CompletedTask);
    }

    public async Task ScoutMovedAsync(Discovery discovery, JobStatus status, CancellationToken cancellationToken)
    {
        if (status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp) && !Ended.Contains(status))
        {
            return;
        }

        if (!await ClaimAsync(discovery.Job, cancellationToken))
        {
            return;
        }

        var confirmed = status == JobStatus.AwaitingReview
            ? await discovery.Scout.Match(scout => review.ConfirmedAsync(scout, cancellationToken), () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]))
            : [];

        await SettleAsync(discovery, confirmed, cancellationToken);

        if (!Ended.Contains(status))
        {
            await DiscardScoutAsync(discovery, cancellationToken);
        }
    }

    public async Task AbandonAsync(Discovery discovery, CancellationToken cancellationToken)
    {
        if (!await ClaimAsync(discovery.Job, cancellationToken))
        {
            return;
        }

        await SettleAsync(discovery, [], cancellationToken);
        await DiscardScoutAsync(discovery, cancellationToken);
    }

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Task<bool> ClaimAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(
            _ => Task.FromResult(book.Of(job).Match(discovery => !discovery.Settled, () => false) && ImmutableInterlocked.Update(ref settling, claimed => claimed.Add(job))),
            cancellationToken);

    private async Task SettleAsync(Discovery discovery, IReadOnlyList<DeclaredCheck> checks, CancellationToken cancellationToken)
    {
        await (checks.Count == 0 ? NoneAsync(discovery, cancellationToken) : FoundAsync(discovery, checks, cancellationToken));
        ImmutableInterlocked.Update(ref settling, claimed => claimed.Remove(discovery.Job));
    }

    private async Task FoundAsync(Discovery discovery, IReadOnlyList<DeclaredCheck> checks, CancellationToken cancellationToken)
    {
        var declaration = CheckDeclaration.Write(checks);
        await jobs.ShipAsync(discovery.Job, declaration, cancellationToken);
        await book.KeepAsync(discovery with { State = DiscoveryState.Found, Declaration = declaration }, cancellationToken);
    }

    private Task NoneAsync(Discovery discovery, CancellationToken cancellationToken) =>
        book.KeepAsync(discovery with { State = DiscoveryState.NoneFound }, cancellationToken);

    private async Task DiscardScoutAsync(Discovery discovery, CancellationToken cancellationToken) =>
        await discovery.Scout.Match(scout => launcher.DiscardAsync(scout, cancellationToken), () => Task.CompletedTask);

    private async Task<Option<Discovery>> UncheckedAsync(JobId job, CancellationToken cancellationToken) =>
        await (await jobs.UncheckedAsync(job, cancellationToken)).Match(
            place => DiscoverAsync(place, cancellationToken),
            () => Task.FromResult(Option<Discovery>.None));

    private async Task<Option<Discovery>> DiscoverAsync(JobPlace place, CancellationToken cancellationToken)
    {
        if (!await launcher.DiscoversAsync(cancellationToken))
        {
            return Option<Discovery>.None;
        }

        var running = new Discovery(place.Job, place.Repository, DiscoveryState.Running, book.Now);

        return await book.LatestFound(place.Repository).Match(
            async found =>
            {
                await FoundAsync(running, found.Checks, cancellationToken);

                return Option<Discovery>.None;
            },
            () => LaunchAsync(place, running, cancellationToken));
    }

    private async Task<Option<Discovery>> LaunchAsync(JobPlace place, Discovery running, CancellationToken cancellationToken) =>
        await (await launcher.LaunchAsync(place, cancellationToken)).Match(
            async scout =>
            {
                var launched = running with { Scout = scout };
                await book.KeepAsync(launched, cancellationToken);

                return Option<Discovery>.Some(launched);
            },
            async _ =>
            {
                await NoneAsync(running, cancellationToken);

                return Option<Discovery>.None;
            });
}
