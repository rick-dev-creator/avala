using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Sdk;

namespace Avala.Jobs.TurnChecks;

internal sealed class NextRound(IEnumerable<IRoundRouter> routers, JobLauncher launcher, HoldJob hold, IEnumerable<IChildRetries> retries)
{
    public async Task<bool> RetriedAsync(Job job, CancellationToken cancellationToken)
    {
        foreach (var retry in retries)
        {
            if (await retry.RetriesAsync(job.Id, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public Task InSameSessionAsync(Job job, Feedback feedback, CancellationToken cancellationToken) =>
        launcher.RetryInSameSessionAsync(job, feedback, cancellationToken);

    public async Task HoldAsync(Job job, HoldReason reason, CancellationToken cancellationToken) =>
        _ = await hold.ExecuteAsync(job, reason, cancellationToken);

    public async Task HoldAsync(Job job, HoldReason reason, Option<UsageLimit> limit, CancellationToken cancellationToken) =>
        _ = await hold.ExecuteAsync(job, reason, limit, cancellationToken);

    public async Task GoOnAsync(Job job, Feedback feedback, Func<Job, Feedback, CancellationToken, Task> retry, CancellationToken cancellationToken)
    {
        var route = await RouteAsync(job, feedback, cancellationToken);

        switch (route.Action)
        {
            case RoundAction.HandOff:
                await route.Handoff.Match(
                    handoff => HandOffAsync(job, feedback, handoff, cancellationToken),
                    () => retry(job, feedback, cancellationToken));
                break;
            case RoundAction.Hold:
                await HoldAsync(job, feedback, route.Hold, cancellationToken);
                break;
            default:
                await retry(job, feedback, cancellationToken);
                break;
        }
    }

    public async Task RelayAsync(Job job, Feedback feedback, Func<Job, Feedback, CancellationToken, Task> retry, CancellationToken cancellationToken)
    {
        var route = await RouteAsync(job, feedback, cancellationToken);

        var moved = route.Action == RoundAction.HandOff && await route.Handoff.Match(
            async handoff => (await launcher.HandOffAfterTurnAsync(job, handoff, relays: true, cancellationToken)).IsSuccess,
            () => Task.FromResult(false));

        if (moved)
        {
            return;
        }

        if (route.Action == RoundAction.GoOn)
        {
            await retry(job, feedback, cancellationToken);
        }

        if (job.State == JobState.Checking)
        {
            await HoldAsync(job, HoldReason.LimitNearlyReached, cancellationToken);
        }
    }

    private Task<RoundRoute> RouteAsync(Job job, Feedback feedback, CancellationToken cancellationToken) =>
        job.Connection.Match(
            connection => AskAsync(new RoundQuestion(job.Id, connection, feedback.Text), cancellationToken),
            () => Task.FromResult(RoundRoute.GoOn));

    private async Task<RoundRoute> AskAsync(RoundQuestion question, CancellationToken cancellationToken)
    {
        foreach (var router in routers)
        {
            var route = await router.RouteAsync(question, cancellationToken);

            if (route.Action != RoundAction.GoOn)
            {
                return route;
            }
        }

        return RoundRoute.GoOn;
    }

    private async Task HandOffAsync(Job job, Feedback feedback, JobHandoff handoff, CancellationToken cancellationToken)
    {
        if ((await launcher.HandOffAfterTurnAsync(job, handoff, cancellationToken)).IsFailure)
        {
            await HoldAsync(job, feedback, HoldReason.LimitNearlyReached, cancellationToken);
        }
    }

    private async Task HoldAsync(Job job, Feedback feedback, HoldReason reason, CancellationToken cancellationToken)
    {
        if (job.Retry(feedback).IsSuccess)
        {
            _ = await hold.ExecuteAsync(job, reason, cancellationToken);
        }
    }
}
