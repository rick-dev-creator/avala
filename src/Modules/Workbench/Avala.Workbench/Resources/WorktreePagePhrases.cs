using System.Globalization;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Resources;

internal static class WorktreePagePhrases
{
    public const string Scope = "The worktrees of Avala's jobs, and what Avala freed";

    public const string NothingLarge = "No worktree takes space right now.";

    public const string NothingUpcoming = "Nothing is waiting to be freed.";

    public const string NothingFreed = "Avala has freed nothing yet.";

    public const string FreeNow = "Free space now";

    private static readonly string[] StateOrder = ["working", "waiting", "finished", "other"];

    public static string Bytes(long bytes) =>
        bytes >= 1L << 30
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.#} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.#} MB");

    public static string Count(int count) => count == 1 ? "1 worktree" : string.Create(CultureInfo.InvariantCulture, $"{count} worktrees");

    public static string Limits(WorktreePolicy policy, Option<DiskSpace> disk)
    {
        var budget = policy.SpaceBudget.Match(bytes => $"of a {Bytes(bytes)} budget", () => "with no space budget");
        var free = disk.Match(found => string.Create(CultureInfo.InvariantCulture, $" · {found.FreeShare * 100:0}% of the disk free"), () => string.Empty);
        var floor = policy.DiskFloor.Match(share => string.Create(CultureInfo.InvariantCulture, $", keeping {share * 100:0}%"), () => string.Empty);

        return $"{budget}{free}{floor}";
    }

    public static string Shortage(WorktreeSpace space)
    {
        var budget = space.Budget.Match(bytes => $" of a {Bytes(bytes)} budget", () => string.Empty);
        var disk = space.Disk.Match(found => string.Create(CultureInfo.InvariantCulture, $", and the disk has {found.FreeShare * 100:0}% free"), () => string.Empty);

        return $"Avala freed every worktree it could and is still past the limits: worktrees take {Bytes(space.Worktrees)}{budget}{disk}. What is left belongs to jobs that are working or could not be freed.";
    }

    public static string States(IEnumerable<Option<BoardJob>> jobs)
    {
        var states = jobs.GroupBy(job => job.Match(found => found.Status switch
            {
                Jobs.Contracts.JobStatus.AwaitingReview or Jobs.Contracts.JobStatus.NeedsHelp => "waiting",
                Jobs.Contracts.JobStatus.Approved or Jobs.Contracts.JobStatus.Discarded or Jobs.Contracts.JobStatus.Failed => "finished",
                _ => "working",
            }, () => "other"))
            .OrderBy(group => Array.IndexOf(StateOrder, group.Key))
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Count()} {group.Key}"));

        return string.Join(", ", states);
    }

    public static string Title(Option<BoardJob> job, string path) =>
        job.Match(found => FirstLine(found.Summary.Instruction), () => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));

    public static string Build(FolderUsage folder) =>
        folder.Bytes <= 0 || folder.BuildBytes <= 0
            ? "no build output"
            : string.Create(CultureInfo.InvariantCulture, $"{folder.BuildBytes * 100.0 / folder.Bytes:0}% build output");

    public static string Moment(WorktreeMoment moment) => moment switch
    {
        WorktreeMoment.Archived => "archived",
        WorktreeMoment.Delivered => "on its base branch",
        WorktreeMoment.PullRequest => "pull request open",
        WorktreeMoment.Undelivered => "approved, not delivered",
        WorktreeMoment.Discarded => "discarded",
        WorktreeMoment.Failed => "failed",
        WorktreeMoment.Idle => "waiting, untouched",
        WorktreeMoment.Space => "to stay within the limits",
        _ => "you asked",
    };

    public static string When(DateTimeOffset due, DateTimeOffset now)
    {
        var left = due - now;

        return left <= TimeSpan.Zero ? "at the next sample"
            : left < TimeSpan.FromHours(1) ? string.Create(CultureInfo.InvariantCulture, $"in {Math.Max(1, (int)left.TotalMinutes)} min")
            : left < TimeSpan.FromDays(1) ? string.Create(CultureInfo.InvariantCulture, $"in {(int)left.TotalHours} h")
            : string.Create(CultureInfo.InvariantCulture, $"in {(int)left.TotalDays} d");
    }

    public static string Event(WorktreeEvent recorded) =>
        recorded.Moment.Match(
            moment => $"freed {Bytes(recorded.Bytes)}, {Moment(moment)}",
            () => "its work deleted");

    public static string Lifetime(IReadOnlyList<WorktreeEvent> history)
    {
        var lived = history.SelectMany(recorded => recorded.Moment.IsSome ? recorded.Lifetime.Match<TimeSpan[]>(lifetime => [lifetime], () => []) : []).ToList();

        return lived.Count == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"A worktree lives {Duration(TimeSpan.FromTicks((long)lived.Average(lifetime => lifetime.Ticks)))} on average before it is freed.");
    }

    public static string Duration(TimeSpan span) =>
        span >= TimeSpan.FromDays(1) ? Measure(span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture), "day")
        : span >= TimeSpan.FromHours(1) ? Measure(span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture), "hour")
        : Measure(Math.Max(1, span.TotalMinutes).ToString("0", CultureInfo.InvariantCulture), "minute");

    private static string Measure(string amount, string unit) => amount == "1" ? $"1 {unit}" : $"{amount} {unit}s";

    public static string Day(WorktreeDay day) =>
        string.Create(CultureInfo.InvariantCulture, $"{day.Created} created · {day.Freed} freed · {Bytes(day.Recovered)} recovered");

    public const string Kept = "kept";

    public static string LeftItsBranch(WorktreeMoment moment) => $"{Moment(moment)} · its checkout left its branch";

    public static string Freed(int count, long bytes, int leftTheirBranch)
    {
        var freed = count == 0
            ? leftTheirBranch == 0 ? "Nothing could be freed: every worktree left belongs to a job that is working." : "Nothing could be freed."
            : $"Freed {Count(count)}, {Bytes(bytes)}.";

        return leftTheirBranch switch
        {
            0 => freed,
            1 => $"{freed} 1 worktree is kept because its checkout left its branch.",
            _ => string.Create(CultureInfo.InvariantCulture, $"{freed} {leftTheirBranch} worktrees are kept because their checkout left its branch."),
        };
    }

    private static string FirstLine(string text)
    {
        var first = text.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();

        return first.Length > 80 ? $"{first[..79]}…" : first;
    }
}
