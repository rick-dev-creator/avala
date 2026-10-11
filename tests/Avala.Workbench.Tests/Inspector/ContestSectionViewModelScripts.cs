using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class ContestSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task ADecidedCandidateShowsItsPlaceAndTheRankingWithTheReasonEachRunCameWhereItDidAsync()
    {
        var (first, second) = (bench.Job("Fix JPY rounding", JobStatus.AwaitingReview).Job, bench.Job("Fix JPY rounding", JobStatus.NeedsHelp).Job);
        Contest(FakeContests.Contest(ContestStatus.Decided, first, second) with
        {
            Verdict = new ContestVerdict(
                [
                    new Standing(1, new CandidateFacts(first, 1, Passed: true, RuleFileEdited: false, 0, 1, 2, DateTimeOffset.UnixEpoch) { Spent = [new Cost(0.009m, "USD")] }),
                    new Standing(2, new CandidateFacts(second, 2, Passed: false, RuleFileEdited: false, 1, 3, 1, DateTimeOffset.UnixEpoch)) { Excluded = Criterion.Verification },
                ],
                first,
                DateTimeOffset.UnixEpoch),
        });
        using var section = new ContestSectionViewModel(bench.Inspected());

        await section.FocusAsync(first, bench);

        Assert.Equal(("candidate 1 of 2 · the winner", "Decided: the winner waits for your review", true), (section.Fact, section.Status, section.IsContested));
        Assert.Equal(
            [
                "1. Candidate 1 · 0 exceptions · 1 attempt · 2 lines changed · 0.009 USD · the only verified run · this job",
                "2. Candidate 2 · not verified · 1 exception · 3 attempts · 1 line changed · not verified",
            ],
            section.Ranking);
    }

    [Fact]
    public async Task BeforeTheVerdictEachCandidateSaysHowItStandsAsync()
    {
        var (first, second) = (bench.Job("Fix JPY rounding", JobStatus.AwaitingReview).Job, bench.Job("Fix JPY rounding", JobStatus.Running).Job);
        Contest(FakeContests.Contest(ContestStatus.Running, first, second) is var running
            ? running with { Candidates = [running.Candidates[0] with { Outcome = CandidateOutcome.AwaitingReview }, running.Candidates[1]] }
            : running);
        using var section = new ContestSectionViewModel(bench.Inspected());

        await section.FocusAsync(second, bench);

        Assert.Equal("candidate 2 of 2", section.Fact);
        Assert.Equal(["Candidate 1 · ready for review", "Candidate 2 · running · this job"], section.Ranking);
    }

    [Fact]
    public async Task AJobOutsideAnyContestHidesTheSectionAsync()
    {
        using var section = new ContestSectionViewModel(bench.Inspected());

        await section.FocusAsync(bench.Job("Fix JPY rounding", JobStatus.Running).Job, bench);

        Assert.Equal((true, false, string.Empty), (section.IsLoaded, section.IsContested, section.Fact));
    }

    public void Dispose() => bench.Dispose();

    private void Contest(ContestRecord contest) =>
        bench.Board.Publish(contest.Candidates.ToImmutableDictionary(candidate => candidate.Job, _ => new BoardContest(contest, 1)));
}
