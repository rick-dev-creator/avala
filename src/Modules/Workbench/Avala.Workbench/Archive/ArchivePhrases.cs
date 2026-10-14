using System.Globalization;
using Avala.Jobs.Contracts;

namespace Avala.Workbench.Archive;

internal static class ArchivePhrases
{
    public const string Empty = "Archived conversations show here";

    public static string Status(JobStatus status) => status switch
    {
        JobStatus.AwaitingReview => "Ready for review",
        JobStatus.Approved => "Approved",
        JobStatus.Discarded => "Discarded",
        JobStatus.Failed => "Failed",
        _ => "Working",
    };

    public static string Summary(int count) =>
        count == 1
            ? "1 conversation, kept with its transcript and records"
            : string.Create(CultureInfo.InvariantCulture, $"{count} conversations, kept with their transcripts and records");

    public static string Ended(DateTimeOffset ended, DateTimeOffset now)
    {
        var since = now - ended;

        return since.TotalMinutes < 1 ? "Just now"
            : since.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)since.TotalMinutes} min ago")
            : since.TotalDays < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)since.TotalHours} h ago")
            : since.TotalDays < 2 ? "Yesterday"
            : since.TotalDays < 7 ? string.Create(CultureInfo.InvariantCulture, $"{(int)since.TotalDays} d ago")
            : ended.ToString("d MMM", CultureInfo.InvariantCulture);
    }
}
