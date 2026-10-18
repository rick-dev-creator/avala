using System.Globalization;
using Avala.Notes.Contracts;

namespace Avala.Workbench.Notes;

internal static class NotePhrases
{
    public const string JobEmpty = "No notes yet. Jot down an idea, a plan or a bug while the job works.";

    public const string ProjectEmpty = "No notes yet. Jot down an idea, a plan or a bug for this project.";

    public const string Untitled = "Untitled";

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
}
