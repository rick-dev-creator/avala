using System.Globalization;
using Avala.Handoffs.Contracts;

namespace Avala.Workbench.Settings;

internal static class LimitPhrases
{
    public const string EveryConnection = "None chosen: a job started on Auto relays to any connection Auto compared. Choose connections so a job started on one of them relays too.";

    public const string Override = "A repository's .avala/jobs.json limits section replaces these for its own jobs.";

    private static readonly (OnLimit Action, string Name)[] ActionNames =
    [
        (OnLimit.Hold, "Hold the job"),
        (OnLimit.HandOffSameHarness, "Hand off on the same harness"),
        (OnLimit.HandOffAnyHarness, "Hand off to any harness"),
    ];

    private static readonly (HandoffStrategy Strategy, string Name, string Explanation)[] StrategyNames =
    [
        (HandoffStrategy.MostHeadroom, "Most headroom", "The connection with the most usage left takes the baton."),
        (HandoffStrategy.InOrder, "In order", "The next connection in the list below takes the baton."),
        (HandoffStrategy.SoonestReset, "Soonest reset", "The connection whose window resets first takes the baton."),
        (HandoffStrategy.RoundRobin, "Round robin", "Connections take the baton in turn, one after another."),
    ];

    public static IReadOnlyList<string> Actions { get; } = [.. ActionNames.Select(named => named.Name)];

    public static IReadOnlyList<string> Strategies { get; } = [.. StrategyNames.Select(named => named.Name)];

    public static string ActionName(OnLimit action) => ActionNames.First(named => named.Action == action).Name;

    public static OnLimit ActionOf(string name) => ActionNames.FirstOrDefault(named => named.Name == name, ActionNames[0]).Action;

    public static string StrategyName(HandoffStrategy strategy) => StrategyNames.First(named => named.Strategy == strategy).Name;

    public static HandoffStrategy StrategyOf(string name) => StrategyNames.FirstOrDefault(named => named.Name == name, StrategyNames[0]).Strategy;

    public static string Explain(HandoffStrategy strategy) => StrategyNames.First(named => named.Strategy == strategy).Explanation;

    public static string Percent(double percent) => $"{Math.Round(percent).ToString("0", CultureInfo.InvariantCulture)}%";

    public static string OnlyThese(int count) => count == 1 ? "Only the chosen connection takes the baton." : $"Only these {count} connections take the baton, in this order.";

    public static string Summary(LimitSettings settings)
    {
        var at = Percent(settings.Threshold * 100);
        var cap = Percent(settings.HardCap * 100);
        var preparing = settings.WindDown ? "winds down, leaving its work in a valid state, and " : string.Empty;
        var reset = settings.Resume == ResumeAtReset.Auto
            ? settings.CompactOnResume ? "compacts and continues by itself at the reset" : "continues by itself at the reset"
            : "asks you before it continues at the reset";

        return settings.OnLimit switch
        {
            OnLimit.HandOffSameHarness => $"At {at} a job {preparing}hands off to another connection of its harness; none passes {cap}. With no connection left, it {reset}.",
            OnLimit.HandOffAnyHarness => $"At {at} a job {preparing}hands off to another connection, on any harness; none passes {cap}. With no connection left, it {reset}.",
            _ => $"Past {at} a job holds when its turn ends, rather than starting another. It {reset}.",
        };
    }

    public static string File(LimitFile file) => file.Status switch
    {
        LimitFileStatus.Absent => "limits.json: not created yet, so these defaults apply",
        LimitFileStatus.Rejected => file.Problem.Match(problem => $"limits.json is rejected ({problem}), so jobs hold at 90% until it is saved again", () => "limits.json is rejected"),
        _ => "limits.json: applied",
    };

    public static string Refused(HandoffError error) => error switch
    {
        HandoffError.InvalidHardCap => "The hard cap cannot be below the point where a job prepares.",
        HandoffError.InvalidThreshold => "A job prepares somewhere between 1% and 100%.",
        HandoffError.InvalidConnections => "Choose at most 16 connections, each once.",
        HandoffError.Unwritable => "limits.json could not be written.",
        _ => $"These limits were refused ({error}).",
    };

    public static string Key(LimitSettings settings) =>
        string.Join('|', settings.OnLimit, Percent(settings.Threshold * 100), Percent(settings.HardCap * 100), settings.Strategy, settings.Resume, settings.WindDown, settings.CompactOnResume, string.Join(',', settings.Connections.Select(connection => connection.Value)));
}
