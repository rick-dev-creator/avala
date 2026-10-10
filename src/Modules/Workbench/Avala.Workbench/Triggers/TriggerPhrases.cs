using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Triggers.Contracts;

namespace Avala.Workbench.Triggers;

internal static class TriggerPhrases
{
    public const string Tunnel =
        "Avala never opens a public port. To receive webhooks from the internet, run your own tunnel to this port, such as Tailscale Funnel.";

    private const int MinutesPerHour = 60;

    private const int MinutesPerDay = 1440;

    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static readonly DayOfWeek[] Weekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static string Scope(TimeZoneInfo zone) => $"Times in this computer's time zone, {zone.Id}";

    public static string Fires(TriggerState trigger) =>
        trigger.Kind switch
        {
            TriggerKind.Interval => Every(trigger.EveryMinutes),
            TriggerKind.FixedTime => $"{Days(trigger.Days)} at {trigger.At.Match(at => at.ToString("HH:mm", CultureInfo.InvariantCulture), () => string.Empty)}",
            _ => $"Webhook · {trigger.Hook.Match(hook => hook, () => string.Empty)}",
        };

    public static string Next(TriggerState trigger, TimeZoneInfo zone) =>
        !trigger.Enabled ? "Disabled"
        : trigger.Kind == TriggerKind.Webhook ? "Runs on each signed delivery"
        : trigger.NextRun.Match(at => $"Next · {Local(at, zone, "ddd d MMM HH:mm")}", () => "Not scheduled");

    public static string Running(TriggerState trigger) =>
        string.Create(CultureInfo.InvariantCulture, $"{trigger.Running} of {trigger.Concurrency} running");

    public static string Target(TriggerState trigger) =>
        $"{(trigger.Target == TriggerTarget.Loop ? "Queues a loop task" : "Starts a job")} · {Level(trigger.Autonomy)} at most";

    public static string Run(TriggerRun run, TimeZoneInfo zone) =>
        $"{Local(run.At, zone, "yyyy-MM-dd HH:mm")} · {Origin(run.Origin)} · {Outcome(run)}{(run.AutonomyCapped ? " · capped to supervised by the repository" : string.Empty)}";

    public static string Delivery(WebhookDelivery delivery, TimeZoneInfo zone) =>
        $"{Local(delivery.At, zone, "yyyy-MM-dd HH:mm:ss")} · {delivery.Path} · {Verdict(delivery.Verdict)}";

    public static string File(TriggerFile file) =>
        file.Status switch
        {
            TriggerFileStatus.Applied => string.Create(CultureInfo.InvariantCulture, $"{file.Path} · applied, {file.Triggers} {(file.Triggers == 1 ? "trigger" : "triggers")}"),
            TriggerFileStatus.Absent => $"{file.Path} · absent",
            _ => $"{file.Path} · rejected: {file.Error.Match(error => error.ToString(), () => "unknown")}",
        };

    public static string Endpoint(WebhookEndpoint endpoint) =>
        endpoint.Url.Match(
            url => $"Listening on {url}<id>, on this computer only.",
            () => endpoint.Problem.Match(
                problem => problem == TriggerError.NoPortLease ? "Webhooks are off: no port could be leased." : "Webhooks are off: the listener could not start.",
                () => "No webhook trigger is declared."));

    public static string Error(TriggerError error) =>
        error == TriggerError.UnknownTrigger ? "That trigger is no longer declared. Reload to see the current ones." : $"Refused: {error}.";

    public static string Fired(TriggerRun run) => $"Run now: {Outcome(run)}.";

    private static string Every(int minutes) =>
        minutes switch
        {
            MinutesPerDay => "Every day",
            MinutesPerHour => "Every hour",
            _ when minutes % MinutesPerDay == 0 => string.Create(CultureInfo.InvariantCulture, $"Every {minutes / MinutesPerDay} days"),
            _ when minutes % MinutesPerHour == 0 => string.Create(CultureInfo.InvariantCulture, $"Every {minutes / MinutesPerHour} hours"),
            1 => "Every minute",
            _ => string.Create(CultureInfo.InvariantCulture, $"Every {minutes} minutes"),
        };

    private static string Days(IReadOnlyList<DayOfWeek> days) =>
        days.Count == 0 || days.Count == 7 ? "Every day"
        : Same(days, Weekdays) ? "Weekdays"
        : Same(days, Weekend) ? "Weekends"
        : string.Join(", ", days.OrderBy(day => ((int)day + 6) % 7).Select(day => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(day)));

    private static bool Same(IReadOnlyList<DayOfWeek> days, DayOfWeek[] set) => days.Count == set.Length && set.All(days.Contains);

    private static string Level(Autonomy autonomy) => autonomy == Autonomy.Autonomous ? "autonomous" : "supervised";

    private static string Origin(TriggerOrigin origin) =>
        origin switch
        {
            TriggerOrigin.Schedule => "on schedule",
            TriggerOrigin.CatchUp => "caught up",
            TriggerOrigin.Webhook => "webhook",
            TriggerOrigin.Manual => "run by you",
            _ => "external",
        };

    private static string Outcome(TriggerRun run) =>
        run.Outcome switch
        {
            RunOutcome.Submitted => "job submitted",
            RunOutcome.Enqueued => run.Job.IsSome ? "taken by the loop" : "queued in the loop",
            RunOutcome.Rejected => $"refused: {run.Rejection.Match(rejection => rejection.ToString(), () => "unknown")}",
            RunOutcome.AtConcurrencyCap => "skipped, at its concurrency cap",
            RunOutcome.Missed => string.Create(CultureInfo.InvariantCulture, $"missed {run.Missed} while Avala was closed"),
            _ => "dropped at restart",
        };

    private static string Verdict(DeliveryVerdict verdict) =>
        verdict switch
        {
            DeliveryVerdict.Accepted => "accepted",
            DeliveryVerdict.NotPost => "not a POST",
            DeliveryVerdict.UnknownTrigger => "no such trigger",
            DeliveryVerdict.TooLarge => "too large",
            DeliveryVerdict.RateLimited => "rate limited",
            DeliveryVerdict.NotSigned => "not signed",
            DeliveryVerdict.NoSecret => "secret not set",
            DeliveryVerdict.BadSignature => "bad signature",
            DeliveryVerdict.Stale => "stale timestamp",
            DeliveryVerdict.Replayed => "replayed",
            DeliveryVerdict.Malformed => "not a JSON object",
            _ => "trigger disabled",
        };

    private static string Local(DateTimeOffset at, TimeZoneInfo zone, string format) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString(format, CultureInfo.InvariantCulture);
}
