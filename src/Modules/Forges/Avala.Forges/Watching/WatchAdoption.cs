using System.Collections.Immutable;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal sealed record Delivered(JobId Job, PullRequestRules Rules, string RemoteUrl, PullRequestRef PullRequest, string Head);

internal sealed class WatchAdoption(WatchBook book, PullRequestReader reader, TimeProvider clock)
{
    private ImmutableHashSet<JobId> delivering = [];

    public async Task<TResult> DeliveringAsync<TResult>(JobId job, Func<Task<TResult>> delivery)
    {
        ImmutableInterlocked.Update(ref delivering, known => known.Add(job));

        try
        {
            return await delivery();
        }
        finally
        {
            ImmutableInterlocked.Update(ref delivering, known => known.Remove(job));
        }
    }

    public Task AdoptFoundAsync(Delivered found, CancellationToken cancellationToken) =>
        Volatile.Read(ref delivering).Contains(found.Job) || book.Find(found.Job).IsSome ? Task.CompletedTask : AdoptAsync(found, cancellationToken);

    public Task RefusedAsync(JobId job, ForgeError error, CancellationToken cancellationToken) => book.RefusedAsync(job, error, cancellationToken);

    public async Task AdoptAsync(Delivered delivered, CancellationToken cancellationToken)
    {
        var poll = await reader.PollAsync(cancellationToken);
        var known = book.Find(delivered.Job);
        var state = known.Match(
            kept => kept.State with
            {
                PullRequest = delivered.PullRequest,
                Policy = delivered.Rules.Policy,
                MaxWakeUps = delivered.Rules.MaxWakeUps,
                Redelivery = delivered.Rules.Redelivery,
                Status = kept.State.Status == WatchStatus.NeedsPerson ? WatchStatus.NeedsPerson : WatchStatus.Watching,
                Ended = Option<WatchEnd>.None,
                Failure = Option<ForgeError>.None,
                Failures = 0,
            },
            () => new PullRequestWatchState(delivered.Job, delivered.Rules.Forge, delivered.PullRequest, delivered.Rules.Policy, delivered.Rules.MaxWakeUps) { Redelivery = delivered.Rules.Redelivery });
        var kept = new KeptWatch(
            state with { NextPoll = clock.GetUtcNow() + poll },
            delivered.Rules.Remote,
            delivered.RemoteUrl,
            known.Match(found => found.Handled, () => []));

        await book.SaveAsync(kept, cancellationToken);
        await book.PublishAsync(new PullRequestOpened(delivered.Job, delivered.Rules.Forge, delivered.PullRequest), cancellationToken);
    }
}
