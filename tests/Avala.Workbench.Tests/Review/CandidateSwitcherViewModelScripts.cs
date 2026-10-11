using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Review;

namespace Avala.Workbench.Tests.Review;

public sealed class CandidateSwitcherViewModelScripts
{
    [Fact]
    public void ADecidedContestOffersEveryCandidateWithItsVerificationDiffSizeAndCostAndMarksTheWinner()
    {
        var (first, second) = (JobId.New(), JobId.New());
        var contest = FakeContests.Contest(ContestStatus.Decided, first, second) with
        {
            Verdict = new ContestVerdict(
                [
                    new Standing(1, new CandidateFacts(second, 2, Passed: true, RuleFileEdited: false, 0, 1, 1, DateTimeOffset.UnixEpoch) { Spent = [new Cost(0.009m, "USD")] }),
                    new Standing(2, new CandidateFacts(first, 1, Passed: false, RuleFileEdited: false, 0, 1, 1, DateTimeOffset.UnixEpoch)) { Excluded = Criterion.Verification },
                ],
                second,
                DateTimeOffset.UnixEpoch),
        };

        ViewModelScript.Given(new CandidateSwitcherViewModel(second))
            .When(switcher => switcher.Track(Contested(contest)))
            .Then(switcher =>
            {
                Assert.Equal((true, "2 candidates · Decided: the winner waits for your review"), (switcher.IsShown, switcher.Heading));
                Assert.Equal(
                    [
                        new CandidateChoice(first, "Candidate 1", "not verified · 1 line changed · no cost reported", IsOpen: false, IsWinner: false),
                        new CandidateChoice(second, "Candidate 2 · winner", "verified · 1 line changed · 0.009 USD", IsOpen: true, IsWinner: true),
                    ],
                    switcher.Choices);
            });
    }

    [Fact]
    public void SwitchingAsksForAnotherCandidateButNeverTheOneAlreadyOpen()
    {
        var (first, second) = (JobId.New(), JobId.New());
        var switcher = new CandidateSwitcherViewModel(first);
        switcher.Track(Contested(FakeContests.Contest(ContestStatus.Running, first, second)));
        var asked = new List<JobId>();
        switcher.Switched += (_, job) => asked.Add(job);

        switcher.SwitchCommand.Execute(switcher.Choices[0]);
        switcher.SwitchCommand.Execute(switcher.Choices[1]);

        Assert.Equal([second], asked);
    }

    [Fact]
    public void AJobOutsideAnyContestHidesTheSwitcher()
    {
        var switcher = new CandidateSwitcherViewModel(JobId.New());

        switcher.Track(ImmutableDictionary<JobId, BoardContest>.Empty);

        Assert.Equal((false, 0), (switcher.IsShown, switcher.Choices.Count));
    }

    private static ImmutableDictionary<JobId, BoardContest> Contested(ContestRecord contest) =>
        contest.Candidates.ToImmutableDictionary(candidate => candidate.Job, _ => new BoardContest(contest, 1));
}
