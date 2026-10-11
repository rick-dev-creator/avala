using System.Globalization;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Triggers;

internal static class ForgePhrases
{
    public static string Fires(ForgeRule rule, Option<ForgeEventWatch> watch) =>
        $"{watch.Match(found => found.Forge.Length > 0 ? found.Forge : rule.Connection, () => rule.Connection)} · {Event(rule)}";

    public static string Next(Option<ForgeEventWatch> watch, TimeZoneInfo zone) =>
        watch.Match(
            found => found.Status switch
            {
                EventWatchStatus.Disabled => "Disabled",
                EventWatchStatus.BackingOff => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Retrying after {found.Failure.Match(error => error.ToString(), () => "a failure")} · {found.Failures} {(found.Failures == 1 ? "failure" : "failures")} · next poll {When(found.NextPoll, zone)}"),
                EventWatchStatus.Watching => $"Next poll · {When(found.NextPoll, zone)}",
                _ => "Waiting for its first poll",
            },
            () => "Not polled: no forge reads its events");

    public static string Entry(ForgeEventEntry entry, TimeZoneInfo zone) =>
        $"{Amounts.Time(entry.At, zone)} · {entry.Trigger.Name} · {What(entry)} by {Actor(entry.Actor)} · {Verdict(entry)}";

    public static string Checked(ForgeEventWatch watch, TimeZoneInfo zone) => $"Checked {watch.Trigger.Name}: {Next(watch, zone)}.";

    public static string Refused(ForgeError error) =>
        error == ForgeError.EventsUnsupported ? "This forge does not read events." : $"Could not check: {error}.";

    private static string Event(ForgeRule rule) =>
        rule.Event switch
        {
            ForgeEventKind.IssueOpened => "issue opened",
            ForgeEventKind.IssueLabeled => $"issue labeled `{Text(rule.Label)}`",
            ForgeEventKind.CommentCommand => $"comment `{Text(rule.Command)}`",
            ForgeEventKind.ChecksFailed => $"checks failed on `{Text(rule.Branch)}`",
            _ => rule.Reviewer.Match(reviewer => $"review requested from `{reviewer}`", () => "review requested"),
        };

    private static string What(ForgeEventEntry entry)
    {
        var number = entry.Number.ToString(CultureInfo.InvariantCulture);

        return entry.Kind switch
        {
            ForgeEventKind.IssueOpened => $"issue #{number} opened",
            ForgeEventKind.IssueLabeled => $"#{number} labeled {Text(entry.Detail)}",
            ForgeEventKind.CommentCommand => $"comment on #{number} `{Text(entry.Detail)}`",
            ForgeEventKind.ChecksFailed => $"checks failed on {Text(entry.Detail)}",
            _ => $"review of #{number} requested from {Text(entry.Detail)}",
        };
    }

    private static string Verdict(ForgeEventEntry entry) =>
        entry.Verdict switch
        {
            ForgeEventVerdict.Fired => entry.Outcome.Match(
                outcome => outcome switch
                {
                    RunOutcome.Submitted => "fired, job submitted",
                    RunOutcome.Enqueued => "fired, queued in the loop",
                    RunOutcome.AtConcurrencyCap => "fired, skipped at its concurrency cap",
                    RunOutcome.Rejected => "fired, refused",
                    _ => "fired",
                },
                () => "fired"),
            ForgeEventVerdict.NotAllowed => "not allowed: not on the authors list",
            ForgeEventVerdict.Duplicate => "duplicate, already fired",
            _ => $"filtered: {entry.Reason.Match(Reason, () => "no match")}",
        };

    private static string Reason(FilterReason reason) =>
        reason switch
        {
            FilterReason.OtherLabel => "another label",
            FilterReason.OtherCommand => "not the command",
            FilterReason.OtherReviewer => "another reviewer",
            FilterReason.FromFork => "a fork's pull request",
            _ => "the trigger is gone",
        };

    private static string Actor(string actor) => actor.Length > 0 ? actor : "someone unknown";

    private static string When(Option<DateTimeOffset> at, TimeZoneInfo zone) =>
        at.Match(moment => TimeZoneInfo.ConvertTime(moment, zone).ToString("HH:mm", CultureInfo.InvariantCulture), () => "not scheduled");

    private static string Text(Option<string> text) => text.Match(found => found, () => string.Empty);
}
