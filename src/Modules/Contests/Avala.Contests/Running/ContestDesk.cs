using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Contests.Running;

internal sealed class ContestDesk(ContestBook book, IJobCatalog catalog, ContestJudge judge, IJobs jobs) : IHandle<JobProgressed>
{
    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (book.OfJob(integrationEvent.Job).IsSome)
        {
            await DiscardAsync(await book.WorkAsync(token => FollowAsync(integrationEvent.Job, token), cancellationToken), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<JobId>> ReconcileAsync(Contest contest, CancellationToken cancellationToken)
    {
        var losers = new List<JobId>();

        foreach (var candidate in contest.Candidates.ToList())
        {
            losers.AddRange(await FollowAsync(contest, candidate.Job, cancellationToken));
        }

        await judge.JudgeAsync(contest, cancellationToken);

        return losers;
    }

    public async Task DiscardAsync(IReadOnlyList<JobId> losers, CancellationToken cancellationToken)
    {
        foreach (var loser in losers)
        {
            _ = await jobs.DiscardAsync(loser, cancellationToken);
        }
    }

    private Task<IReadOnlyList<JobId>> FollowAsync(JobId job, CancellationToken cancellationToken) =>
        book.FindOf(job).Match(contest => FollowAsync(contest, job, cancellationToken), () => Task.FromResult<IReadOnlyList<JobId>>([]));

    private async Task<IReadOnlyList<JobId>> FollowAsync(Contest contest, JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            history => ApplyAsync(contest, job, Outcome(history.Summary), cancellationToken),
            () => Task.FromResult<IReadOnlyList<JobId>>([]));

    private async Task<IReadOnlyList<JobId>> ApplyAsync(Contest contest, JobId job, CandidateOutcome outcome, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case CandidateOutcome.Approved:
                return await PickAsync(contest, job, cancellationToken);
            case CandidateOutcome.Running:
                if (contest.Reopen(job).IsSuccess)
                {
                    await book.KeepAsync(contest, record => new CandidateReopened(record, job), cancellationToken);
                }

                return [];
            default:
                await SettleAsync(contest, job, outcome, cancellationToken);

                return [];
        }
    }

    private async Task SettleAsync(Contest contest, JobId job, CandidateOutcome outcome, CancellationToken cancellationToken)
    {
        if (!contest.Settle(job, outcome, judge.Now).TryGetValue(out var settled, out _) || !settled.Changed)
        {
            return;
        }

        await (settled.Now == ContestStatus.Cancelled
            ? book.KeepAsync(contest, record => new ContestClosed(record), cancellationToken)
            : book.KeepAsync(contest, record => new CandidateSettled(record, job), cancellationToken));
        await judge.JudgeAsync(contest, cancellationToken);
    }

    private async Task<IReadOnlyList<JobId>> PickAsync(Contest contest, JobId job, CancellationToken cancellationToken)
    {
        if (!contest.Close(job, judge.Now).TryGetValue(out var picked, out _))
        {
            return [];
        }

        await book.KeepAsync(contest, record => new ContestClosed(record), cancellationToken);

        return picked.Losers;
    }

    private static CandidateOutcome Outcome(JobSummary summary) => summary.Status switch
    {
        JobStatus.AwaitingReview => CandidateOutcome.AwaitingReview,
        JobStatus.NeedsHelp when summary.Hold.IsNone => CandidateOutcome.NeedsHelp,
        JobStatus.Failed => CandidateOutcome.Failed,
        JobStatus.Discarded => CandidateOutcome.Discarded,
        JobStatus.Approved => CandidateOutcome.Approved,
        _ => CandidateOutcome.Running,
    };
}
