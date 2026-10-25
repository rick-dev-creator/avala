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

    public static string Shortage(WorktreeSpace space) =>
        $"Avala freed every worktree it could and still needs space: worktrees take {Bytes(space.Worktrees)}, all of them from jobs that are working.";

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
        span >= TimeSpan.FromDays(1) ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalDays:0.#} days")
        : span >= TimeSpan.FromHours(1) ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalHours:0.#} hours")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, span.TotalMinutes):0} minutes");

    public static string Day(WorktreeDay day) =>
        string.Create(CultureInfo.InvariantCulture, $"{day.Created} created · {day.Freed} freed · {Bytes(day.Recovered)} recovered");

    public static string Freed(int count, long bytes) =>
        count == 0 ? "Nothing could be freed: every worktree belongs to a job that is working." : $"Freed {Count(count)}, {Bytes(bytes)}.";

    private static string FirstLine(string text)
    {
        var first = text.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();

        return first.Length > 80 ? $"{first[..79]}…" : first;
    }
}
