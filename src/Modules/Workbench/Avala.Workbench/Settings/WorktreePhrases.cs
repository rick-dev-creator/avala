using System.Globalization;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Settings;

internal sealed record WorktreeChoice(string Label, Option<TimeSpan> After);

internal sealed record WorktreeRule(string Title, string Explanation, IReadOnlyList<WorktreeChoice> Choices, Option<TimeSpan> Current);

internal sealed record LimitChoice(string Label, Option<double> Value);

internal sealed record WorktreeLimit(string Title, string Explanation, IReadOnlyList<LimitChoice> Choices, Option<double> Current);

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
        (WorktreeMoment.Idle, "Waiting for you, untouched", "Awaiting review or held, with no agent running."),
    ];

    private static readonly double[] Budgets = [5, 10, 20, 50, 100];

    private static readonly double[] Floors = [5, 10, 15, 20];

    public const string BudgetTitle = "Space for worktrees";

    public const string BudgetNote = "Past it, the worktrees idle the longest are freed first. A worktree whose job is working is never freed.";

    public const string FloorTitle = "Free disk to keep";

    public const string FloorNote = "Below it, worktrees are freed the same way until enough of the disk is free.";

    public const double Gigabyte = 1L << 30;

    public static IEnumerable<(WorktreeMoment Moment, string Title, string Explanation)> Shown => Moments;

    public static IReadOnlyList<WorktreeChoice> FreeChoices(Option<TimeSpan> current) =>
        WithCurrent([new("Keep it", Option<TimeSpan>.None), new("Free it at once", TimeSpan.Zero), .. Delays.Select(delay => new WorktreeChoice($"Free it after {Duration(delay)}", delay))], current, FreeLabel);

    public static IReadOnlyList<WorktreeChoice> DeleteChoices(Option<TimeSpan> current) =>
        WithCurrent([new("Never", Option<TimeSpan>.None), new("At once", TimeSpan.Zero), .. DeletionDelays.Select(delay => new WorktreeChoice($"After {Duration(delay)}", delay))], current, DeleteLabel);

    public static IReadOnlyList<LimitChoice> BudgetChoices(Option<double> current) =>
        WithCurrent([.. Budgets.Select(gigabytes => new LimitChoice(Gigabytes(gigabytes), gigabytes)), new LimitChoice("No limit", Option<double>.None)], current, Gigabytes);

    public static IReadOnlyList<LimitChoice> FloorChoices(Option<double> current) =>
        WithCurrent([.. Floors.Select(percent => new LimitChoice(Percent(percent), percent)), new LimitChoice("No floor", Option<double>.None)], current, Percent);

    public static string Key(WorktreePolicy policy) =>
        string.Join(
            ',',
            new[] { policy.Archived, policy.Delivered, policy.PullRequest, policy.Undelivered, policy.Discarded, policy.Failed, policy.DeleteDiscarded, policy.DeleteFailed, policy.Idle }
                .Select(span => span.Match(kept => kept.TotalHours.ToString(CultureInfo.InvariantCulture), () => "keep"))
                .Append(policy.SpaceBudget.Match(bytes => bytes.ToString(CultureInfo.InvariantCulture), () => "none"))
                .Append(policy.DiskFloor.Match(share => share.ToString(CultureInfo.InvariantCulture), () => "none")));

    public static bool Same(Option<double> left, Option<double> right) =>
        left.Match(one => right.Match(other => Math.Abs(one - other) < 1e-9, () => false), () => right.IsNone);

    public static string Gigabytes(double gigabytes) => $"{gigabytes.ToString("0.##", CultureInfo.InvariantCulture)} GB";

    public static string Percent(double percent) => $"{percent.ToString("0.##", CultureInfo.InvariantCulture)}%";

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

    private static IReadOnlyList<LimitChoice> WithCurrent(IReadOnlyList<LimitChoice> choices, Option<double> current, Func<double, string> label) =>
        choices.Any(choice => Same(choice.Value, current))
            ? choices
            : [.. choices, new LimitChoice(current.Match(label, () => string.Empty), current)];

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
