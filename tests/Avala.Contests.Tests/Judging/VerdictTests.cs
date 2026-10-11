using Avala.Agents.Contracts.Events;
using Avala.Contests.Contracts;
using Avala.Contests.Judging;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Tests.Judging;

public sealed class VerdictTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<Criterion> Criteria { get; } =
    [
        Criterion.Exceptions, Criterion.Attempts, Criterion.DiffSize, Criterion.Cost, Criterion.Finish, Criterion.Order,
    ];

    [Theory]
    [MemberData(nameof(Criteria))]
    public void TheFirstCriterionThatTellsTwoPassingRunsApartDecidesWhichWins(Criterion criterion)
    {
        var better = Better(criterion, Facts(1));
        var worse = Worse(criterion, Facts(2) with { Ordinal = 2 });

        var verdict = Verdicts.Rank([worse, better], At);

        Assert.Equal(Option<JobId>.Some(better.Job), verdict.Winner);
        Assert.Equal(
            [(1, better.Job, Option<Criterion>.Some(criterion)), (2, worse.Job, Option<Criterion>.None)],
            verdict.Ranking.Select(standing => (standing.Place, standing.Facts.Job, standing.Ahead)));
    }

    [Fact]
    public void ARunWhoseVerificationDidNotPassIsExcludedAndRankedAfterThePassingOnes()
    {
        var failed = Facts(1) with { Passed = false, LinesChanged = 1 };
        var passed = Facts(2) with { LinesChanged = 400 };

        var verdict = Verdicts.Rank([failed, passed], At);

        Assert.Equal(Option<JobId>.Some(passed.Job), verdict.Winner);
        Assert.Equal((failed.Job, Option<Criterion>.Some(Criterion.Verification)), (verdict.Ranking[1].Facts.Job, verdict.Ranking[1].Excluded));
    }

    [Fact]
    public void ARunThatEditedARuleFileIsDisqualifiedEvenWithTheSmallestDiff()
    {
        var rewrote = Facts(1) with { RuleFileEdited = true, LinesChanged = 2 };
        var honest = Facts(2) with { LinesChanged = 40 };

        var verdict = Verdicts.Rank([rewrote, honest], At);

        Assert.Equal(Option<JobId>.Some(honest.Job), verdict.Winner);
        Assert.Equal(Option<Criterion>.Some(Criterion.RuleFiles), verdict.Ranking.Single(standing => standing.Facts.Job == rewrote.Job).Excluded);
    }

    [Fact]
    public void NoPassingRunLeavesTheVerdictWithoutAWinnerButRanksEveryCandidate()
    {
        var verdict = Verdicts.Rank([Facts(2) with { Passed = false }, Facts(1) with { Passed = false }], At);

        Assert.Equal(Option<JobId>.None, verdict.Winner);
        Assert.Equal([1, 2], verdict.Ranking.Select(standing => standing.Facts.Ordinal));
    }

    private static CandidateFacts Facts(int ordinal) =>
        new(JobId.New(), ordinal, Passed: true, RuleFileEdited: false, Exceptions: 1, Attempts: 2, LinesChanged: 10, At) { Spent = [new Cost(0.05m, "USD")] };

    private static CandidateFacts Better(Criterion criterion, CandidateFacts facts) => criterion switch
    {
        Criterion.Exceptions => facts with { Exceptions = 0, Attempts = 3, LinesChanged = 50, Spent = [new Cost(0.09m, "USD")], Finished = At.AddMinutes(9) },
        Criterion.Attempts => facts with { Attempts = 1, LinesChanged = 50, Spent = [new Cost(0.09m, "USD")], Finished = At.AddMinutes(9) },
        Criterion.DiffSize => facts with { LinesChanged = 3, Spent = [new Cost(0.09m, "USD")], Finished = At.AddMinutes(9) },
        Criterion.Cost => facts with { Spent = [new Cost(0.01m, "USD")], Finished = At.AddMinutes(9) },
        Criterion.Finish => facts with { Finished = At.AddMinutes(-1) },
        _ => facts,
    };

    private static CandidateFacts Worse(Criterion criterion, CandidateFacts facts) => criterion switch
    {
        Criterion.Exceptions => facts with { Exceptions = 2 },
        Criterion.Attempts => facts with { Attempts = 2 },
        Criterion.DiffSize => facts with { LinesChanged = 30 },
        Criterion.Cost => facts with { Spent = [new Cost(0.02m, "USD"), new Cost(0.03m, "USD")] },
        Criterion.Finish => facts,
        _ => facts,
    };
}
