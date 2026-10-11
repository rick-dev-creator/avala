using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Contests.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Presenting;

internal static class ContestPhrases
{
    public const string Spread = "Spread across connections";

    public const string SpreadNote = "Each candidate runs on the next usable connection in turn, instead of all on the one a job would take now.";

    public static string Candidates(int count) => Count(count, "candidate", "candidates");

    public static string Share(int candidates, BudgetCaps caps)
    {
        if (candidates < 2)
        {
            return string.Empty;
        }

        string[] shares =
        [
            .. caps.CostPerJob.Select(cap => Amounts.Costs([new Cost(cap.Amount / candidates, cap.Currency)])),
            .. caps.TokensPerJob.Match<string[]>(tokens => [Amounts.Tokens(tokens / candidates)], () => []),
        ];

        return shares.Length == 0
            ? "The repository caps no job, so the candidates spend without a share."
            : $"Each candidate may spend {Fraction(candidates)} of a job's budget: {Joined(shares)}.";
    }

    public static string BestOf(Result<BestOf, ContestRejection> declared) =>
        declared.Match(
            bestOf => bestOf.Max < 2
                ? "The repository's .avala/jobs.json allows one candidate per job."
                : $"Up to {bestOf.Max} candidates; the best verified run is kept and the others are discarded once you approve it.",
            rejection => $"The repository's .avala/jobs.json bestOf section is rejected: {Rejection(rejection)} One candidate per job.");

    public static string Rejection(ContestRejection rejection) => rejection switch
    {
        ContestRejection.Unreadable => "it cannot be read.",
        ContestRejection.TooLarge => "the file is too large.",
        ContestRejection.Malformed => "it is not valid.",
        ContestRejection.UnknownField => "it has a field the format does not define.",
        ContestRejection.InvalidCandidates => "candidates must be a whole number from 1 to max.",
        ContestRejection.InvalidMax => "max must be a whole number from 1 to 8.",
        ContestRejection.TooFewCandidates => "a contest needs at least two candidates.",
        ContestRejection.TooManyCandidates => "the repository allows fewer candidates.",
        _ => "a candidate job was refused: check the repository and the connection.",
    };

    public static string Row(ContestRecord contest, int passed) =>
        Joined([
            Candidates(contest.Candidates.Count),
            string.Create(CultureInfo.InvariantCulture, $"{passed} passed"),
            .. contest.Picked.Match<string[]>(
                picked => [.. contest.Candidates.Where(candidate => candidate.Job == picked).Select(candidate => string.Create(CultureInfo.InvariantCulture, $"picked candidate {candidate.Ordinal}"))],
                () => []),
        ]);

    public static string Status(ContestRecord contest) => contest.Status switch
    {
        ContestStatus.Running => "Waiting for every candidate to settle",
        ContestStatus.Judging => "Judging the candidates",
        ContestStatus.Decided => "Decided: the winner waits for your review",
        ContestStatus.Undecided => "No candidate passed: continue one, or discard them",
        ContestStatus.Closed => "Closed: the approved candidate was kept",
        _ => "Cancelled: every candidate ended without approval",
    };

    public static string Fact(ContestRecord contest, Jobs.Contracts.JobId job)
    {
        var ordinal = contest.Candidates.FirstOrDefault(candidate => candidate.Job == job)?.Ordinal ?? 0;
        var place = contest.Verdict.Match(verdict => verdict.Ranking.FirstOrDefault(standing => standing.Facts.Job == job)?.Place ?? 0, () => 0);
        var role = contest.Picked.Match(picked => picked == job ? "picked" : "discarded", () => contest.Verdict.Bind(verdict => verdict.Winner).Match(
            winner => winner == job ? "the winner" : string.Create(CultureInfo.InvariantCulture, $"place {place}"),
            () => string.Empty));

        return Joined([string.Create(CultureInfo.InvariantCulture, $"candidate {ordinal} of {contest.Candidates.Count}"), .. role.Length > 0 ? [role] : Array.Empty<string>()]);
    }

    public static IReadOnlyList<string> Ranking(ContestRecord contest, Jobs.Contracts.JobId job) =>
        contest.Verdict.Match<IReadOnlyList<string>>(
            verdict => [.. verdict.Ranking.Select(standing => Joined([Standing(standing), Reason(standing), .. standing.Facts.Job == job ? ["this job"] : Array.Empty<string>()]))],
            () => [.. contest.Candidates.Select(candidate => Joined([
                string.Create(CultureInfo.InvariantCulture, $"Candidate {candidate.Ordinal}"),
                Outcome(candidate.Outcome),
                .. candidate.Job == job ? ["this job"] : Array.Empty<string>(),
            ]))]);

    public static string Outcome(CandidateOutcome outcome) => outcome switch
    {
        CandidateOutcome.AwaitingReview => "ready for review",
        CandidateOutcome.NeedsHelp => "needs help: retries ran out",
        CandidateOutcome.Failed => "failed",
        CandidateOutcome.Discarded => "discarded",
        CandidateOutcome.Approved => "approved",
        _ => "running",
    };

    public static string Choice(Standing standing) =>
        Joined([
            standing.Excluded.Match(Excluded, () => "verified"),
            Count(standing.Facts.LinesChanged, "line changed", "lines changed"),
            standing.Facts.Spent.Count > 0 ? Amounts.Costs(standing.Facts.Spent) : "no cost reported",
        ]);

    public static string Standing(Standing standing) =>
        Joined([
            string.Create(CultureInfo.InvariantCulture, $"{standing.Place}. Candidate {standing.Facts.Ordinal}"),
            Count(standing.Facts.Exceptions, "exception", "exceptions"),
            Count(standing.Facts.Attempts, "attempt", "attempts"),
            Count(standing.Facts.LinesChanged, "line changed", "lines changed"),
            .. standing.Facts.Spent.Count > 0 ? [Amounts.Costs(standing.Facts.Spent)] : Array.Empty<string>(),
        ]);

    public static string Reason(Standing standing) =>
        standing.Excluded.Match(
            Excluded,
            () => standing.Ahead.Match(criterion => $"ahead of the next on {Criterion(criterion)}", () => standing.Place == 1 ? "the only verified run" : "last of the verified runs"));

    public static string Criterion(Criterion criterion) => criterion switch
    {
        Contests.Contracts.Criterion.Exceptions => "fewer evidence exceptions",
        Contests.Contracts.Criterion.Attempts => "fewer attempts",
        Contests.Contracts.Criterion.DiffSize => "a smaller diff",
        Contests.Contracts.Criterion.Cost => "a lower cost",
        Contests.Contracts.Criterion.Finish => "an earlier finish",
        _ => "the order it started in",
    };

    private static string Excluded(Criterion criterion) =>
        criterion == Contests.Contracts.Criterion.RuleFiles ? "disqualified: it edited a rule file" : "not verified";

    private static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    private static string Joined(string[] parts) => string.Join(" · ", parts);

    private static string Fraction(int candidates) => candidates switch
    {
        2 => "half",
        3 => "a third",
        4 => "a quarter",
        _ => string.Create(CultureInfo.InvariantCulture, $"1/{candidates}"),
    };
}
