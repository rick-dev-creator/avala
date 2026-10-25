using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal sealed record FoundChecks(DiscoveryState State, IReadOnlyList<DeclaredCheck> Checks, bool Shipped)
{
    public static FoundChecks None { get; } = new(DiscoveryState.NoneFound, [], Shipped: false);
}

internal sealed class DiscoveredChecks(DiscoveryDesk desk, DiscoveryBook book, ShippedChecks shipped, JobWorktrees worktrees)
{
    public async Task<FoundChecks> OfAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        await desk.StartAsync(attempt.Job, cancellationToken);
        var own = book.Of(attempt.Job);
        var discovery = own.IsSome ? own : await InheritedAsync(attempt.Job, cancellationToken);

        return await discovery.Match(
            found => found.State switch
            {
                DiscoveryState.Running or DiscoveryState.Proposed => Task.FromResult(new FoundChecks(DiscoveryState.Running, [], Shipped: false)),
                DiscoveryState.Found => FoundAsync(attempt.WorkingDirectory, found, commits: own.IsSome, cancellationToken),
                _ => Task.FromResult(FoundChecks.None),
            },
            () => Task.FromResult(FoundChecks.None));
    }

    private async Task<FoundChecks> FoundAsync(string worktree, Discovery discovery, bool commits, CancellationToken cancellationToken) =>
        new(DiscoveryState.Found, discovery.Checks, await shipped.ShippedAsync(worktree, discovery.Declaration, commits, cancellationToken));

    private async Task<Option<Discovery>> InheritedAsync(JobId job, CancellationToken cancellationToken)
    {
        var parent = (await worktrees.SummaryAsync(job, cancellationToken)).Bind(summary => summary.Companion ? Option<JobId>.None : summary.Parent);

        return await parent.Match(
            async found => book.Of(found).IsSome ? book.Of(found) : await InheritedAsync(found, cancellationToken),
            () => Task.FromResult(Option<Discovery>.None));
    }
}
