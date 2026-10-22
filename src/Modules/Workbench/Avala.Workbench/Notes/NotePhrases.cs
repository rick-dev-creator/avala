using System.Globalization;
using Avala.Notes.Contracts;

namespace Avala.Workbench.Notes;

internal static class NotePhrases
{
    public const string JobEmpty = "No notes yet. Jot down an idea, a plan or a bug while the job works.";

    public const string ProjectEmpty = "No notes yet. Jot down an idea, a plan or a bug for this project.";

    public const string NoMatch = "No note matches. Search covers every job and project.";

    public const string Untitled = "Untitled";

    public static string JobStarted(string idea) => $"Started a job from \"{idea}\". It will ask its questions first.";

    public static string IssuePublished(Uri link) => $"Published as an issue: {link}";

    public static string Images(int count) =>
        count switch
        {
            0 => string.Empty,
            1 => "1 image",
            _ => string.Create(CultureInfo.InvariantCulture, $"{count} images"),
        };

    public static string Excerpt(string body) =>
        body.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.StartsWith("![", StringComparison.Ordinal)) ?? string.Empty;

    public static string Rejection(NoteRejection rejection) =>
        rejection switch
        {
            NoteRejection.UnknownNote => "This note no longer exists.",
            NoteRejection.UntitledNote => "Give the note a title before saving it.",
            NoteRejection.Archived => "This note is archived and can no longer change.",
            NoteRejection.NotAnImage => "Only images can be added to a note.",
            NoteRejection.MissingMedia => "The image could not be found.",
            NoteRejection.MediaTooLarge => "The image is too large to keep in a note.",
            _ => "The note could not be written to disk.",
        };

    public static string Rejection(IdeaRejection rejection) =>
        rejection switch
        {
            IdeaRejection.UnknownNote => "This note no longer exists.",
            IdeaRejection.NotAnIdea => "Make the note an idea before starting a job from it.",
            IdeaRejection.IssueRefused => "The issue could not be published. Check that the repository has a forge you are signed in to.",
            _ => "The job could not be started.",
        };
}
