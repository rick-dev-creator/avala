using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal sealed record Delivered(JobId Job, PullRequestRules Rules, string RemoteUrl, PullRequestRef PullRequest, string Head);

internal sealed class WatchAdoption(WatchBook book, PullRequestReader reader, TimeProvider clock)
{
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
                Status = kept.State.Status == WatchStatus.NeedsPerson ? WatchStatus.NeedsPerson : WatchStatus.Watching,
                Ended = Option<WatchEnd>.None,
                Failure = Option<ForgeError>.None,
                Failures = 0,
            },
            () => new PullRequestWatchState(delivered.Job, delivered.Rules.Forge, delivered.PullRequest, delivered.Rules.Policy, delivered.Rules.MaxWakeUps));
        var kept = new KeptWatch(
            state with { NextPoll = clock.GetUtcNow() + poll },
            delivered.Rules.Remote,
            delivered.RemoteUrl,
            known.Match(found => found.Handled, () => []));

        await book.SaveAsync(kept, cancellationToken);
        await book.PublishAsync(new PullRequestOpened(delivered.Job, delivered.Rules.Forge, delivered.PullRequest), cancellationToken);
    }
}
