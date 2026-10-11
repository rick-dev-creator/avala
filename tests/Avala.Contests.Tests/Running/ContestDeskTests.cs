using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Contests.Tests.Running;

public sealed class ContestDeskTests
{
    [Fact]
    public async Task OnceEveryCandidateHasSettledTheContestIsDecidedOnTheFactsGatheredFromEachRunAsync()
    {
        await using var arena = new Arena();
        var started = await arena.StartAsync(4);
        var (first, second, third, fourth) = (started.Candidates[0].Job, started.Candidates[1].Job, started.Candidates[2].Job, started.Candidates[3].Job);

        await arena.AwaitingReviewAsync(first, new RunFacts(Verified: true, Attempts: 2, Added: 5, Removed: 3, Spent: 0.30m));
        await arena.AwaitingReviewAsync(second, new RunFacts(Verified: true, Attempts: 1, Added: 2, Removed: 1, Spent: 0.20m, ExceptionReason.RuleFileEdited));
        await arena.AwaitingReviewAsync(third, new RunFacts(Verified: true, Attempts: 1, Added: 4, Removed: 0, Spent: 0.10m, ExceptionReason.Denial, ExceptionReason.VerificationNotPassed));
        var decided = await arena.AwaitingReviewAsync(fourth, new RunFacts(Verified: false, Attempts: 3, Added: 1, Removed: 0, Spent: 0.50m, ExceptionReason.VerificationNotPassed));

        var verdict = Outcomes.Present(decided.Verdict);
        var at = arena.Clock.GetUtcNow();
        Assert.Equal((ContestStatus.Decided, Option<JobId>.Some(first)), (decided.Status, verdict.Winner));
        Assert.Equal(
            [
                (first, 1, true, false, 0, 2, 8, at.AddMinutes(-3), 0.30m, Option<Criterion>.None),
                (third, 3, true, false, 1, 1, 4, at.AddMinutes(-1), 0.10m, Option<Criterion>.None),
                (second, 2, true, true, 0, 1, 3, at.AddMinutes(-2), 0.20m, Option<Criterion>.Some(Criterion.RuleFiles)),
                (fourth, 4, false, false, 0, 3, 1, at, 0.50m, Option<Criterion>.Some(Criterion.Verification)),
            ],
            verdict.Ranking.Select(standing => (
                standing.Facts.Job,
                standing.Facts.Ordinal,
                standing.Facts.Passed,
                standing.Facts.RuleFileEdited,
                standing.Facts.Exceptions,
                standing.Facts.Attempts,
                standing.Facts.LinesChanged,
                standing.Facts.Finished,
                Assert.Single(standing.Facts.Spent).Amount,
                standing.Excluded)));
        Assert.Equal((decided.Contest, ContestStatus.Decided), arena.Announced<ContestDecided>(found => found.Contest));
    }

    [Fact]
    public async Task AContestWhoseCandidatesAllNeedHelpIsLeftUndecidedAsync()
    {
        await using var arena = new Arena();
        var started = await arena.StartAsync(2);

        await arena.MovedAsync(started.Candidates[0].Job, JobStatus.NeedsHelp);
        var left = await arena.MovedAsync(started.Candidates[1].Job, JobStatus.NeedsHelp);

        Assert.Equal((ContestStatus.Undecided, Option<JobId>.None), (left.Status, Outcomes.Present(left.Verdict).Winner));
        Assert.All(left.Candidates, candidate => Assert.Equal(CandidateOutcome.NeedsHelp, candidate.Outcome));
        Assert.Equal((left.Contest, ContestStatus.Undecided), arena.Announced<ContestUndecided>(found => found.Contest));
    }

    [Fact]
    public async Task ACandidateHeldOnItsWayIsStillRunningAsync()
    {
        await using var arena = new Arena();
        var started = await arena.StartAsync(2);

        var contest = await arena.MovedAsync(started.Candidates[0].Job, JobStatus.NeedsHelp, HoldReason.BudgetExceeded);

        Assert.Equal((ContestStatus.Running, CandidateOutcome.Running), (contest.Status, contest.Candidates[0].Outcome));
        Assert.Empty(arena.Bus.Published.OfType<CandidateSettled>());
    }

    [Fact]
    public async Task ApprovingACandidateClosesTheContestWithItPickedAndDiscardsTheOthersStillInTheRunningAsync()
    {
        await using var arena = new Arena();
        var started = await arena.StartAsync(3);
        var (first, second, third) = (started.Candidates[0].Job, started.Candidates[1].Job, started.Candidates[2].Job);
        await arena.MovedAsync(first, JobStatus.Failed);
        await arena.AwaitingReviewAsync(second, new RunFacts(Verified: true, Attempts: 1, Added: 1, Removed: 1, Spent: 0.10m));

        var closed = await arena.MovedAsync(second, JobStatus.Approved);

        Assert.Equal((ContestStatus.Closed, Option<JobId>.Some(second)), (closed.Status, closed.Picked));
        Assert.Equal([third], arena.Jobs.Discarded);
        Assert.Equal((closed.Contest, ContestStatus.Closed), arena.Announced<ContestClosed>(found => found.Contest));
    }

    [Fact]
    public async Task AContestWhoseCandidatesAllFailOrAreDiscardedIsCancelledAsync()
    {
        await using var arena = new Arena();
        var started = await arena.StartAsync(2);

        await arena.MovedAsync(started.Candidates[0].Job, JobStatus.Failed);
        var cancelled = await arena.MovedAsync(started.Candidates[1].Job, JobStatus.Discarded);

        Assert.Equal(
            (ContestStatus.Cancelled, CandidateOutcome.Failed, CandidateOutcome.Discarded),
            (cancelled.Status, cancelled.Candidates[0].Outcome, cancelled.Candidates[1].Outcome));
        Assert.Equal((cancelled.Contest, ContestStatus.Cancelled), arena.Announced<ContestClosed>(found => found.Contest));
        Assert.Empty(arena.Bus.Published.OfType<ContestUndecided>());
    }

    [Fact]
    public async Task ASettledCandidateThatRunsAgainReopensADecidedContestAsync()
    {
        await using var arena = new Arena();
        var started = await arena.StartAsync(2);
        var (first, second) = (started.Candidates[0].Job, started.Candidates[1].Job);
        await arena.AwaitingReviewAsync(first, new RunFacts(Verified: true, Attempts: 1, Added: 1, Removed: 0, Spent: 0.10m));
        await arena.AwaitingReviewAsync(second, new RunFacts(Verified: true, Attempts: 1, Added: 2, Removed: 0, Spent: 0.10m));

        var reopened = await arena.MovedAsync(first, JobStatus.Running);

        Assert.Equal((ContestStatus.Running, CandidateOutcome.Running, Option<ContestVerdict>.None), (reopened.Status, reopened.Candidates[0].Outcome, reopened.Verdict));
        Assert.Equal((reopened.Contest, ContestStatus.Running), arena.Announced<CandidateReopened>(found => found.Contest));
        Assert.Equal(first, Assert.Single(arena.Bus.Published.OfType<CandidateReopened>()).Candidate);
    }
}
