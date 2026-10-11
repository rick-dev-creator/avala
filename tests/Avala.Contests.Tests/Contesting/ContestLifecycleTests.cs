using Avala.Agents.Contracts.Connections;
using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Contests.Tests.Contesting;

public sealed class ContestLifecycleTests
{
    [Theory]
    [InlineData(1, 4, nameof(ContestError.TooFewCandidates))]
    [InlineData(5, 4, nameof(ContestError.TooManyCandidates))]
    [InlineData(9, 12, nameof(ContestError.TooManyCandidates))]
    public void AContestRunsFromTwoCandidatesUpToTheMostTheRepositoryAllows(int candidates, int max, string expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(Contest.Sized(candidates, max)).ToString());

    [Fact]
    public void CandidatesAreNumberedInTheOrderTheyEntered()
    {
        var contest = Given.Contest(3);

        Assert.Equal([1, 2, 3], contest.Candidates.Select(candidate => candidate.Ordinal));
        Assert.All(contest.Candidates, candidate => Assert.Equal(CandidateOutcome.Running, candidate.Outcome));
    }

    [Fact]
    public void SettlingTheLastCandidateStartsJudgingTheNextRound()
    {
        var contest = Given.Contest();
        var first = Outcomes.Succeeds(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.AwaitingReview, Given.At));

        var last = Outcomes.Succeeds(contest.Settle(contest.Candidates[1].Job, CandidateOutcome.NeedsHelp, Given.At));

        Assert.Equal((ContestStatus.Running, ContestStatus.Judging, 1), (first.Now, last.Now, contest.Round));
    }

    [Fact]
    public void SettlingACandidateAgainWithTheSameOutcomeChangesNothing()
    {
        var contest = Given.ContestIn(ContestStatus.Decided);

        var settled = Outcomes.Succeeds(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.AwaitingReview, Given.At));

        Assert.Equal((false, ContestStatus.Decided, 1), (settled.Changed, contest.Status, contest.Round));
    }

    [Fact]
    public void ACandidateThatSettlesDifferentlyAfterTheVerdictIsJudgedAgainInANewRound()
    {
        var contest = Given.ContestIn(ContestStatus.Decided);

        _ = Outcomes.Succeeds(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.Discarded, Given.At));

        Assert.Equal((ContestStatus.Judging, 2), (contest.Status, contest.Round));
    }

    [Fact]
    public void AVerdictOfAnEarlierRoundIsStale()
    {
        var contest = Given.ContestIn(ContestStatus.Judging);
        var earlier = contest.Round;
        _ = Outcomes.Succeeds(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.NeedsHelp, Given.At));

        Assert.Equal(ContestError.StaleVerdict, Outcomes.FailsWith(contest.Decide(earlier, Given.Verdict(contest.Candidates[1].Job))));
        Assert.Equal(ContestStatus.Judging, contest.Status);
    }

    [Fact]
    public void AVerdictWithoutAWinnerLeavesTheContestUndecided()
    {
        var contest = Given.ContestIn(ContestStatus.Judging);

        var reached = Outcomes.Succeeds(contest.Decide(contest.Round, Given.Verdict()));

        Assert.Equal((Option<JobId>.None, ContestStatus.Undecided), (reached.Winner, contest.Status));
    }

    [Fact]
    public void AReopenedCandidateTakesTheContestBackToRunningAndDropsTheVerdict()
    {
        var contest = Given.ContestIn(ContestStatus.Decided);

        _ = Outcomes.Succeeds(contest.Reopen(contest.Candidates[1].Job));

        Assert.Equal((ContestStatus.Running, CandidateOutcome.Running, false), (contest.Status, contest.Candidates[1].Outcome, contest.Verdict.IsSome));
    }

    [Fact]
    public void CandidatesThatAllEndedWithoutApprovalCancelTheContest()
    {
        var contest = Given.Contest();
        _ = Outcomes.Succeeds(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.Failed, Given.At));

        var last = Outcomes.Succeeds(contest.Settle(contest.Candidates[1].Job, CandidateOutcome.Discarded, Given.At));

        Assert.Equal((ContestStatus.Cancelled, Option<JobId>.None), (last.Now, contest.Picked));
    }

    [Fact]
    public void ClosingPicksTheApprovedCandidateAndNamesTheOpenOnesAsLosers()
    {
        var contest = Given.Contest(3);
        var (first, second, third) = (contest.Candidates[0].Job, contest.Candidates[1].Job, contest.Candidates[2].Job);
        _ = Outcomes.Succeeds(contest.Settle(third, CandidateOutcome.Failed, Given.At));

        var picked = Outcomes.Succeeds(contest.Close(second, Given.At));

        Assert.Equal([first], picked.Losers);
        Assert.Equal((ContestStatus.Closed, Option<JobId>.Some(second), CandidateOutcome.Approved), (contest.Status, contest.Picked, contest.Candidates[1].Outcome));
    }

    [Fact]
    public void AJobOutsideTheContestIsAnUnknownCandidate()
    {
        var contest = Given.Contest();

        Assert.Equal(
            [ContestError.UnknownCandidate, ContestError.UnknownCandidate, ContestError.UnknownCandidate],
            [
                Outcomes.FailsWith(contest.Settle(JobId.New(), CandidateOutcome.AwaitingReview, Given.At)),
                Outcomes.FailsWith(contest.Reopen(JobId.New())),
                Outcomes.FailsWith(contest.Close(JobId.New(), Given.At)),
            ]);
    }

    [Fact]
    public void ACandidateCannotSettleAsRunningOrApproved()
    {
        var contest = Given.Contest();

        Assert.Equal(
            [ContestError.CannotSettle, ContestError.CannotSettle],
            [
                Outcomes.FailsWith(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.Running, Given.At)),
                Outcomes.FailsWith(contest.Settle(contest.Candidates[0].Job, CandidateOutcome.Approved, Given.At)),
            ]);
    }

    [Fact]
    public void ARestoredContestIsTheContestItsRecordDescribes()
    {
        var contest = Given.ContestIn(ContestStatus.Decided);

        var restored = Outcomes.Succeeds(Contest.Restore(contest.Record));

        Assert.Equivalent(contest.Record, restored.Record);
    }

    [Fact]
    public void EachCandidateKeepsTheConnectionItEnteredOn()
    {
        var contest = Outcomes.Succeeds(Contest.Start(
            ContestId.New(),
            "/code/shop-api",
            "Fix JPY rounding",
            [(JobId.New(), new ConnectionName("work")), (JobId.New(), new ConnectionName("personal"))],
            max: 4,
            Given.At,
            spread: true));

        Assert.Equal(["work", "personal"], contest.Candidates.Select(candidate => candidate.Connection.Match(name => name.Value, () => string.Empty)));
        Assert.True(contest.Spread);
    }
}
