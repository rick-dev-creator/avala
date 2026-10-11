using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Notices;
using Avala.Sdk;

namespace Avala.Notifications.Composing;

internal sealed record OutgoingMessage(NotificationKind Kind, Severity Severity, string Title, string Body, DateTimeOffset At)
{
    public Option<JobId> Job { get; init; }

    public string Repository { get; init; } = string.Empty;

    public int Count { get; init; } = 1;
}

internal static class Composer
{
    public const int LinesShown = 5;

    public const int LongestDetail = 120;

    public static OutgoingMessage Compose(IReadOnlyList<Notice> notices, DetailLevel detail, Func<string, string> redact, DateTimeOffset at) =>
        notices is [var single]
            ? Single(single, detail, redact, at)
            : Several(NotificationKind.Summary, notices.Any(notice => notice.Severity == Severity.Urgent) ? $"{notices.Count} things need you" : $"{notices.Count} updates", notices, at);

    public static OutgoingMessage Digest(IReadOnlyList<Notice> notices, DateTimeOffset at) =>
        Several(NotificationKind.Digest, notices.Count == 1 ? "While quiet: 1 update" : $"While quiet: {notices.Count} updates", notices, at);

    public static OutgoingMessage Test(DateTimeOffset at) =>
        new(NotificationKind.Test, Severity.Info, "Avala test notification", "Notifications from Avala reach this device.", at);

    public static string Phrase(NotificationKind kind, int count) =>
        (kind, count) switch
        {
            (NotificationKind.Decision, > 1) => $"{count} decisions need you",
            (NotificationKind.Decision, _) => "A decision needs you",
            (NotificationKind.Held, _) => "A job is held",
            (NotificationKind.Budget, _) => "A job is held by its budget or a limit",
            (NotificationKind.Failed, _) => "A job failed",
            (NotificationKind.LoopEnded, _) => "A loop ended",
            (NotificationKind.Review, _) => "A job is ready for review",
            _ => "Avala",
        };

    private static OutgoingMessage Single(Notice notice, DetailLevel detail, Func<string, string> redact, DateTimeOffset at)
    {
        var lines = new List<string> { notice.Title };

        if (detail == DetailLevel.Detailed)
        {
            lines.AddRange(notice.Detail.Match<string[]>(text => [Cut(redact(text))], () => []));
        }

        return new OutgoingMessage(notice.Kind, notice.Severity, Headline(Phrase(notice.Kind, notice.Count), notice.Repository), string.Join('\n', lines.Where(line => line.Length > 0)), at)
        {
            Job = notice.Job,
            Repository = notice.Repository,
            Count = notice.Count,
        };
    }

    private static OutgoingMessage Several(NotificationKind kind, string title, IReadOnlyList<Notice> notices, DateTimeOffset at)
    {
        var lines = notices.Take(LinesShown).Select(notice => string.Join(" · ", new[] { Phrase(notice.Kind, notice.Count), notice.Repository, notice.Title }.Where(part => part.Length > 0))).ToList();

        if (notices.Count > LinesShown)
        {
            lines.Add($"and {notices.Count - LinesShown} more");
        }

        var repositories = notices.Select(notice => notice.Repository).Distinct(StringComparer.Ordinal).ToList();

        return new OutgoingMessage(kind, notices.Any(notice => notice.Severity == Severity.Urgent) ? Severity.Urgent : Severity.Info, title, string.Join('\n', lines), at)
        {
            Repository = repositories is [var only] ? only : string.Empty,
            Count = notices.Sum(notice => notice.Count),
        };
    }

    private static string Headline(string phrase, string repository) => repository.Length > 0 ? $"{phrase} · {repository}" : phrase;

    private static string Cut(string text)
    {
        var flat = string.Join(' ', text.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flat.Length <= LongestDetail ? flat : $"{flat[..(LongestDetail - 1)]}…";
    }
}
