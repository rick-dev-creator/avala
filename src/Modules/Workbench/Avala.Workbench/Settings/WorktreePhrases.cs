using System.Globalization;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Settings;

internal sealed record WorktreeChoice(string Label, Option<TimeSpan> After);

internal sealed record WorktreeRule(string Title, string Explanation, IReadOnlyList<WorktreeChoice> Choices, Option<TimeSpan> Current);

internal static class WorktreePhrases
{
    public const string Note = "What happens to the worktree of a job that is no longer working. A freed worktree keeps its work and is rebuilt when its job continues.";

    public const string DeletionNote = "Deleting removes the freed work for good. Approved work is never deleted.";

    private static readonly TimeSpan[] Delays = [TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromDays(3), TimeSpan.FromDays(7), TimeSpan.FromDays(30)];

    private static readonly TimeSpan[] DeletionDelays = [TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30), TimeSpan.FromDays(90)];

    private static readonly (WorktreeMoment Moment, string Title, string Explanation)[] Moments =
    [
        (WorktreeMoment.Archived, "Archived", "You archived its conversation."),
        (WorktreeMoment.Delivered, "Approved and on its base branch", "Merged by Avala, through its pull request or by hand."),
        (WorktreeMoment.PullRequest, "Approved, pull request open", "Rebuilt if the pull request wakes the job."),
        (WorktreeMoment.Undelivered, "Approved, not delivered yet", "Approved to keep, and not merged."),
        (WorktreeMoment.Discarded, "Discarded", "You discarded the job."),
        (WorktreeMoment.Failed, "Failed", "The job failed."),
    ];

    public static IEnumerable<(WorktreeMoment Moment, string Title, string Explanation)> Shown => Moments;

    public static IReadOnlyList<WorktreeChoice> FreeChoices(Option<TimeSpan> current) =>
        WithCurrent([new("Keep it", Option<TimeSpan>.None), new("Free it at once", TimeSpan.Zero), .. Delays.Select(delay => new WorktreeChoice($"Free it after {Duration(delay)}", delay))], current, FreeLabel);

    public static IReadOnlyList<WorktreeChoice> DeleteChoices(Option<TimeSpan> current) =>
        WithCurrent([new("Never", Option<TimeSpan>.None), new("At once", TimeSpan.Zero), .. DeletionDelays.Select(delay => new WorktreeChoice($"After {Duration(delay)}", delay))], current, DeleteLabel);

    public static string Key(WorktreePolicy policy) =>
        string.Join(
            ',',
            new[] { policy.Archived, policy.Delivered, policy.PullRequest, policy.Undelivered, policy.Discarded, policy.Failed, policy.DeleteDiscarded, policy.DeleteFailed }
                .Select(span => span.Match(kept => kept.TotalHours.ToString(CultureInfo.InvariantCulture), () => "keep")));

    public static string File(ResourceSettings settings) => $"resources.json: {SettingsPhrases.Status(settings.File, settings.Error)}";

    public static string Refused(ResourceError error) => error switch
    {
        ResourceError.Unwritable => "resources.json could not be written.",
        ResourceError.Unreadable or ResourceError.TooLarge => "resources.json could not be read, so it was left as it is.",
        _ => $"resources.json is not valid ({error}), so it was left as it is. Fix it or remove it, then save again.",
    };

    public static string Duration(TimeSpan span) =>
        span >= TimeSpan.FromDays(1) && span.Ticks % TimeSpan.TicksPerDay == 0 ? Count(span.TotalDays, "day")
        : Count(span.TotalHours, "hour");

    private static string FreeLabel(TimeSpan span) => span == TimeSpan.Zero ? "Free it at once" : $"Free it after {Duration(span)}";

    private static string DeleteLabel(TimeSpan span) => span == TimeSpan.Zero ? "At once" : $"After {Duration(span)}";

    private static IReadOnlyList<WorktreeChoice> WithCurrent(IReadOnlyList<WorktreeChoice> choices, Option<TimeSpan> current, Func<TimeSpan, string> label) =>
        choices.Any(choice => choice.After == current)
            ? choices
            : [.. choices, new WorktreeChoice(current.Match(label, () => string.Empty), current)];

    private static string Count(double amount, string unit)
    {
        var shown = amount.ToString("0.##", CultureInfo.InvariantCulture);

        return $"{shown} {unit}{(shown == "1" ? string.Empty : "s")}";
    }
}
