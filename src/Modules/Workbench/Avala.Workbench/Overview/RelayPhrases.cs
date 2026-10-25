using System.Globalization;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Overview;

internal static class RelayPhrases
{
    public static string Caption(RelayJourney journey) =>
        $"{OverviewPhrases.Count(journey.Connections.Count, "connection")} · {OverviewPhrases.Count(journey.Handoffs, "handoff")}";

    public static string Headline(RelayLeg leg, JobStatus status) => leg.Kind switch
    {
        RelayLegKind.Running => $"Running on {leg.Connection.Value}",
        RelayLegKind.Waiting => $"Waiting for {leg.Connection.Value} to reset",
        _ => status switch
        {
            JobStatus.Approved => $"Approved after {leg.Connection.Value}",
            JobStatus.Discarded => $"Discarded on {leg.Connection.Value}",
            JobStatus.Failed => $"Failed on {leg.Connection.Value}",
            JobStatus.AwaitingReview => $"Ready for review from {leg.Connection.Value}",
            _ => $"Stopped on {leg.Connection.Value}",
        },
    };

    public static string Subline(RelayJourney journey, RelayLeg leg) => leg.Kind switch
    {
        RelayLegKind.Waiting => leg.ResumesAt.Match(
            at => $"Every connection is near its limit · resumes {Clock(at)}{(leg.Compacting ? ", compacted first" : string.Empty)}",
            () => "Every connection is near its limit · no reset time reported"),
        _ => journey.Legs.Count > 1 && journey.Legs[^2] is var before && before.Connection != leg.Connection
            ? $"Took over from {before.Connection.Value}{Used(before)} · {Clock(leg.From)}"
            : $"Since {Clock(leg.From)}",
    };

    public static string Title(RelayLeg leg) =>
        leg.Kind == RelayLegKind.Waiting ? $"Waiting for {leg.Connection.Value}" : leg.Connection.Value;

    public static string Detail(RelayLeg leg, Option<double> used) => leg.Kind switch
    {
        RelayLegKind.Waiting => leg.Compacting ? "Compacts, then resumes at its reset" : leg.ResumesAt.Match(at => $"Resumes at {Clock(at)}", () => "Waits for its reset"),
        RelayLegKind.Running => used.Match(fraction => $"Running · {Amounts.Percent(fraction)} of its window", () => "Running"),
        _ => $"Ran {Span(leg.Length)}{Ending(leg)}",
    };

    public static string Reading(Option<DateTimeOffset> resets) =>
        resets.Match(at => $"resets {Clock(at)}", () => "no reset reported");

    public static string Footer(IReadOnlyList<RelayGauge> gauges, DateTimeOffset now) =>
        gauges.SelectMany(gauge => gauge.Tightest.Bind(limit => limit.ResetsAt).Match<(string Name, DateTimeOffset At)[]>(at => at > now ? [(gauge.Connection.Value, at)] : [], () => []))
            .OrderBy(reset => reset.At)
            .Select(reset => $"{reset.Name} resets first, at {Clock(reset.At)}")
            .FirstOrDefault("No reset reported yet");

    public static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Span(TimeSpan span) =>
        span.TotalMinutes < 1 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)span.TotalSeconds)}s")
        : span.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m");

    private static string Ending(RelayLeg leg) =>
        leg.Ending.Match(
            mark => mark.Kind == RelayMarkKind.Capped ? $" · capped{At(mark.Why)}" : $" · wound down{At(mark.Why)}",
            () => leg.Handoff.Match(handoff => $" · handed off{At(handoff.Why)}", () => string.Empty));

    private static string Used(RelayLeg leg) =>
        leg.Ending.Bind(mark => mark.Why).Match(why => $" at {Amounts.Percent(why.Used)}", () => leg.Handoff.Bind(handoff => handoff.Why).Match(why => $" at {Amounts.Percent(why.Used)}", () => string.Empty));

    private static string At(Option<LimitReason> why) => why.Match(reason => $" at {Amounts.Percent(reason.Used)}", () => string.Empty);
}
