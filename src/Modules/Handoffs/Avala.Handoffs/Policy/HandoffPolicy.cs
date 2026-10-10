using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Policy;

internal sealed record ConnectionState(ConnectionName Connection, string Provider, IReadOnlyList<UsageLimit> Limits);

internal sealed record Situation(ConnectionState Own, Option<ConnectionChoice> Choice, LimitRules Rules, IReadOnlyList<ConnectionState> Usable, DateTimeOffset Now);

internal enum Verdict
{
    GoOn,
    HandOff,
    Wait,
}

internal sealed record ResumePoint(ConnectionName Connection, string Window, Option<DateTimeOffset> At);

internal sealed record Decision(Verdict Verdict)
{
    public static Decision GoOn { get; } = new(Verdict.GoOn);

    public Option<ConnectionChoice> Choice { get; init; }

    public Option<LimitReason> Why { get; init; }

    public Option<ResumePoint> Resume { get; init; }
}

internal static class HandoffPolicy
{
    public static Decision ByThreshold(Situation situation) =>
        Decide(situation, Capacity.Of(situation.Own.Limits, situation.Rules.Threshold, situation.Now));

    public static Decision WhenHeld(Situation situation) =>
        Decide(situation, Capacity.Held(situation.Own.Limits, situation.Rules.Threshold, situation.Now));

    public static IReadOnlyList<ConnectionState> Candidates(Situation situation)
    {
        if (situation.Rules.OnLimit == OnLimit.Hold)
        {
            return [];
        }

        var own = situation.Own.Connection;
        var listed = situation.Rules.Connections;
        var pool = situation.Choice.Match(
            choice => choice.Compared.Select(candidate => candidate.Connection).Where(name => listed.Count == 0 || listed.Contains(name)),
            () => listed.Contains(own) ? listed : []);

        return
        [
            .. pool.Distinct()
                .Where(name => name != own)
                .SelectMany(name => situation.Usable.Where(usable => usable.Connection == name).Take(1))
                .Where(usable => situation.Rules.OnLimit == OnLimit.HandOffAnyHarness || usable.Provider == situation.Own.Provider),
        ];
    }

    private static Decision Decide(Situation situation, Availability own) =>
        own.Window.Match(
            window =>
            {
                var why = new LimitReason(situation.Own.Connection, window.Window, window.UsedFraction, situation.Rules.Threshold);
                var candidates = Candidates(situation);
                var measured = candidates.Select(candidate => Capacity.Measure(candidate.Connection, candidate.Limits, situation.Rules.Threshold, situation.Now)).ToList();

                var resume = Earliest(situation, own, window, candidates);

                return measured.Where(candidate => candidate.Available).MinBy(candidate => candidate.Used) is { } best
                    ? new Decision(Verdict.HandOff)
                    {
                        Why = why,
                        Resume = resume,
                        Choice = new ConnectionChoice(
                            best.Connection,
                            ChoiceReason.MostCapacity,
                            [new CandidateCapacity(situation.Own.Connection, window.UsedFraction, window, situation.Rules.Threshold, Available: false), .. measured],
                            situation.Now),
                    }
                    : new Decision(Verdict.Wait) { Why = why, Resume = resume };
            },
            () => Decision.GoOn);

    private static ResumePoint Earliest(Situation situation, Availability own, UsageLimit window, IReadOnlyList<ConnectionState> candidates)
    {
        var points = candidates
            .Select(candidate => (candidate.Connection, Availability: Capacity.Of(candidate.Limits, situation.Rules.Threshold, situation.Now)))
            .Prepend((Connection: situation.Own.Connection, Availability: own))
            .SelectMany(point => point.Availability.At.Match(
                at => point.Availability.Window.Match<ResumePoint[]>(limit => [new ResumePoint(point.Connection, limit.Window, at)], () => []),
                () => []))
            .OrderBy(point => point.At.Match(at => at, () => DateTimeOffset.MaxValue))
            .ToList();

        return points.Count > 0 ? points[0] : new ResumePoint(situation.Own.Connection, window.Window, Option<DateTimeOffset>.None);
    }
}
