using Avala.Notes.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeNotes : INotes
{
    private readonly List<NoteRecord> kept = [];

    public Option<NoteRejection> Refusal { get; set; }

    public NoteRecord Add(NotePlace place, string title, string body, DateTimeOffset when, NoteState state = NoteState.Kept)
    {
        var note = new NoteRecord(NoteId.New(), place, title, body, [], state, when, when);
        kept.Add(note);

        return note;
    }

    public NoteRecord Find(NoteId note) => kept.Single(found => found.Id == note);

    public ValueTask<Result<NoteRecord, NoteRejection>> TakeAsync(NotePlace place, NoteDraft draft, CancellationToken cancellationToken) =>
        AnswerAsync(() => Add(place, draft.Title, draft.Body, DateTimeOffset.UnixEpoch.AddDays(kept.Count + 1)));

    public ValueTask<Result<NoteRecord, NoteRejection>> EditAsync(NoteId note, NoteDraft draft, CancellationToken cancellationToken) =>
        AnswerAsync(() => Replace(Find(note) with { Title = draft.Title, Body = draft.Body }));

    public ValueTask<Result<NoteRecord, NoteRejection>> AttachAsync(NoteId note, string file, string caption, CancellationToken cancellationToken) =>
        AnswerAsync(() => Replace(Find(note) with { Media = [.. Find(note).Media, new NoteMedia(Path.GetFileName(file), "image/png")] }));

    public ValueTask<Result<NoteRecord, NoteRejection>> MakeIdeaAsync(NoteId note, CancellationToken cancellationToken) =>
        AnswerAsync(() => Replace(Find(note) with { State = NoteState.Idea }));

    public ValueTask<Result<NoteRecord, NoteRejection>> ArchiveAsync(NoteId note, CancellationToken cancellationToken) =>
        AnswerAsync(() => Replace(Find(note) with { State = NoteState.Archived }));

    public ValueTask<IReadOnlyList<NoteRecord>> ListAsync(NotePlace place, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<NoteRecord>>([.. kept.Where(note => note.Place == place).OrderByDescending(note => note.Created)]);

    public ValueTask<IReadOnlyList<NoteRecord>> SearchAsync(string query, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<NoteRecord>>([.. kept
            .Where(note => note.State != NoteState.Archived && (note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || note.Body.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(note => note.Updated)]);

    public ValueTask<Option<NoteRecord>> OpenAsync(NoteId note, CancellationToken cancellationToken) =>
        ValueTask.FromResult(kept.Where(found => found.Id == note).Select(found => Option<NoteRecord>.Some(found)).FirstOrDefault());

    private NoteRecord Replace(NoteRecord note)
    {
        kept[kept.FindIndex(found => found.Id == note.Id)] = note;

        return note;
    }

    private ValueTask<Result<NoteRecord, NoteRejection>> AnswerAsync(Func<NoteRecord> answer) =>
        ValueTask.FromResult(Refusal.Match(refused => Result<NoteRecord, NoteRejection>.Failure(refused), () => Result<NoteRecord, NoteRejection>.Success(answer())));
}
