using System.Globalization;
using Avala.Handoffs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Usage;

namespace Avala.Workbench.Presenting;

internal static class HandoffPhrases
{
    private static readonly TimeSpan SameDay = TimeSpan.FromHours(24);

    public static string Moved(HandoffRecord handoff) => $"Handed off to {handoff.To.Value}{Reason(handoff)}";

    public static string Moves(HandoffRecord handoff) => $"Handed off from {handoff.From.Value} to {handoff.To.Value}{Reason(handoff)}";

    public static Option<string> Model(HandoffRecord handoff) =>
        handoff.Model.Map(fallback => fallback.Wanted.Model.Match(
            wanted => $"model {wanted} not offered by {handoff.To.Value}, ran with its default {fallback.RanWith.Model.Match(model => model, () => "model")}",
            () => $"effort {fallback.Wanted.Effort.Match(effort => effort, () => "chosen")} not offered by {handoff.To.Value}, ran with its default {fallback.RanWith.Effort.Match(effort => $"{effort} effort", () => "effort")}"));

    public static string Waits(ResetWait wait) =>
        wait.ResumesAt.Match(
            at => $"resumes at {Clock(at, wait.Since)} when the {Window(wait.Window)} resets",
            () => "near its limit · no reset time reported, waits for you");

    public static string Window(string window)
    {
        var label = UsagePhrases.Window(window);

        return char.IsUpper(label[0]) ? char.ToLowerInvariant(label[0]) + label[1..] : label;
    }

    private static string Reason(HandoffRecord handoff) =>
        handoff.Why.Match(why => $" at {Amounts.Percent(why.Used)} of the {Window(why.Window)}", () => string.Empty);

    private static string Clock(DateTimeOffset at, DateTimeOffset since) =>
        at - since < SameDay ? at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : Amounts.Time(at);
}
