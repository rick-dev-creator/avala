using Avala.Notes.Contracts;
using Avala.Sdk;

namespace Avala.Notes.Notebook;

internal static class Note
{
    public static Result<NoteRecord, NoteError> Taken(NoteId id, NotePlace place, NoteDraft draft, DateTimeOffset at) =>
        Titled(draft).Map(titled => new NoteRecord(id, place, titled.Title, titled.Body, [], NoteState.Kept, at, at));

    public static Result<NoteRecord, NoteError> Edited(NoteRecord note, NoteDraft draft, DateTimeOffset at) =>
        Open(note).Bind(_ => Titled(draft)).Map(titled => note with { Title = titled.Title, Body = titled.Body, Updated = at });

    public static Result<NoteRecord, NoteError> Attached(NoteRecord note, NoteMedia media, string caption, DateTimeOffset at) =>
        Open(note).Map(open => open with
        {
            Media = [.. open.Media.Where(kept => kept.File != media.File), media],
            Body = $"{open.Body.TrimEnd()}{(open.Body.Trim().Length == 0 ? string.Empty : "\n\n")}![{Escaped(caption)}]({Uri.EscapeDataString(media.File)})",
            Updated = at,
        });

    public static Result<NoteRecord, NoteError> Ideated(NoteRecord note, DateTimeOffset at) =>
        Open(note).Map(open => open.State == NoteState.Idea ? open : open with { State = NoteState.Idea, Updated = at });

    public static Result<NoteRecord, NoteError> Archived(NoteRecord note, DateTimeOffset at) =>
        Open(note).Map(open => open with { State = NoteState.Archived, Updated = at });

    public static Result<NoteRecord, NoteError> Open(NoteRecord note) =>
        note.State == NoteState.Archived ? NoteError.Archived : note;

    private static Result<NoteDraft, NoteError> Titled(NoteDraft draft) =>
        draft.Title.Trim() is { Length: > 0 } title
            ? new NoteDraft(title.ReplaceLineEndings(" "), draft.Body.ReplaceLineEndings("\n").Trim('\n'))
            : NoteError.UntitledNote;

    private static string Escaped(string caption) => caption.ReplaceLineEndings(" ").Replace("]", "\\]", StringComparison.Ordinal).Trim();
}
