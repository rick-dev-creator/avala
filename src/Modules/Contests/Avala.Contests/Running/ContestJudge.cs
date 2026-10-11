using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Contests.Judging;

namespace Avala.Contests.Running;

internal sealed class ContestJudge(FactGatherer gatherer, ContestBook book, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task JudgeAsync(Contest contest, CancellationToken cancellationToken)
    {
        if (contest.Status != ContestStatus.Judging)
        {
            return;
        }

        var round = contest.Round;
        var facts = new List<CandidateFacts>();

        foreach (var candidate in contest.Candidates)
        {
            facts.Add(await gatherer.GatherAsync(candidate, cancellationToken));
        }

        var verdict = Verdicts.Rank(facts, clock.GetUtcNow());

        if (contest.Decide(round, verdict).TryGetValue(out var reached, out _))
        {
            await (reached.Winner.IsSome
                ? book.KeepAsync(contest, record => new ContestDecided(record), cancellationToken)
                : book.KeepAsync(contest, record => new ContestUndecided(record), cancellationToken));
        }
    }
}
