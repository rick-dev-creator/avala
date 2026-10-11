using Avala.Agents.Contracts.Connections;
using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Contests.Tests.Contesting;

internal static class Given
{
    public static DateTimeOffset At { get; } = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public static Contest Contest(int candidates = 2) =>
        Outcomes.Succeeds(Avala.Contests.Contesting.Contest.Start(
            ContestId.New(),
            "/code/shop-api",
            "Fix JPY rounding in invoice totals",
            [.. Enumerable.Range(0, candidates).Select(_ => (JobId.New(), Option<ConnectionName>.None))],
            max: 4,
            At));

    public static Contest ContestIn(ContestStatus status)
    {
        var contest = Contest();
        var (first, second) = (contest.Candidates[0].Job, contest.Candidates[1].Job);

        switch (status)
        {
            case ContestStatus.Judging:
                SettleAll(contest);
                break;
            case ContestStatus.Decided:
                SettleAll(contest);
                _ = Outcomes.Succeeds(contest.Decide(contest.Round, Verdict(first)));
                break;
            case ContestStatus.Undecided:
                SettleAll(contest);
                _ = Outcomes.Succeeds(contest.Decide(contest.Round, Verdict()));
                break;
            case ContestStatus.Closed:
                _ = Outcomes.Succeeds(contest.Close(first, At));
                break;
            case ContestStatus.Cancelled:
                _ = Outcomes.Succeeds(contest.Settle(first, CandidateOutcome.Discarded, At));
                _ = Outcomes.Succeeds(contest.Settle(second, CandidateOutcome.Failed, At));
                break;
        }

        Assert.Equal(status, contest.Status);

        return contest;
    }

    public static ContestVerdict Verdict(params JobId[] winner) =>
        new([], winner.Length > 0 ? winner[0] : Option<JobId>.None, At);

    private static void SettleAll(Contest contest)
    {
        foreach (var candidate in contest.Candidates.ToList())
        {
            _ = Outcomes.Succeeds(contest.Settle(candidate.Job, CandidateOutcome.AwaitingReview, At));
        }
    }
}
