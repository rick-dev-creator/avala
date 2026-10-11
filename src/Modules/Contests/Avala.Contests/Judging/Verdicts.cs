using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Judging;

internal static class Verdicts
{
    private static readonly (Criterion Criterion, Func<CandidateFacts, IComparable> Key)[] TieBreakers =
    [
        (Criterion.Exceptions, facts => facts.Exceptions),
        (Criterion.Attempts, facts => facts.Attempts),
        (Criterion.DiffSize, facts => facts.LinesChanged),
        (Criterion.Cost, facts => facts.Spent.Sum(cost => cost.Amount)),
        (Criterion.Finish, facts => facts.Finished),
        (Criterion.Order, facts => facts.Ordinal),
    ];

    public static ContestVerdict Rank(IReadOnlyList<CandidateFacts> candidates, DateTimeOffset at)
    {
        var eligible = TieBreakers.Skip(1).Aggregate(
            candidates.Where(IsEligible).OrderBy(TieBreakers[0].Key),
            (ordered, tieBreaker) => ordered.ThenBy(tieBreaker.Key)).ToList();
        var excluded = candidates.Where(facts => !IsEligible(facts)).OrderBy(facts => facts.Ordinal);
        Standing[] ranking =
        [
            .. eligible.Select((facts, index) => new Standing(index + 1, facts)
            {
                Ahead = index + 1 < eligible.Count ? AheadBy(facts, eligible[index + 1]) : Option<Criterion>.None,
            }),
            .. excluded.Select((facts, index) => new Standing(eligible.Count + index + 1, facts)
            {
                Excluded = facts.Passed ? Criterion.RuleFiles : Criterion.Verification,
            }),
        ];

        return new ContestVerdict(ranking, eligible.Count > 0 ? eligible[0].Job : Option<JobId>.None, at);
    }

    private static bool IsEligible(CandidateFacts facts) => facts.Passed && !facts.RuleFileEdited;

    private static Criterion AheadBy(CandidateFacts better, CandidateFacts next) =>
        TieBreakers
            .Where(tieBreaker => tieBreaker.Key(better).CompareTo(tieBreaker.Key(next)) != 0)
            .Select(tieBreaker => tieBreaker.Criterion)
            .DefaultIfEmpty(Criterion.Order)
            .First();
}
