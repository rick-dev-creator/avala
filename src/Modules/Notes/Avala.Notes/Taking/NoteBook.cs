using Avala.Notes.Contracts;
using Avala.Notes.Notebook;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Notes.Taking;

internal sealed class NoteBook(INoteShelf shelf, NotePlaces places, IEventBus bus, TimeProvider time) : INotes, IAsyncDisposable
{
    private readonly SerialExecutor serial = new();

    private readonly Dictionary<NoteId, NoteRecord> known = [];

    public async ValueTask<Result<NoteRecord, NoteRejection>> TakeAsync(NotePlace place, NoteDraft draft, CancellationToken cancellationToken)
    {
        var root = await places.RootOfAsync(place, cancellationToken);

        return await serial.RunAsync(
            token => WrittenAsync(Rejected(Note.Taken(NoteId.New(), place, draft, time.GetUtcNow())).Map(note => note with { Root = root }), note => new NoteTaken(note), token),
            cancellationToken);
    }

    public ValueTask<Result<NoteRecord, NoteRejection>> EditAsync(NoteId note, NoteDraft draft, CancellationToken cancellationToken) =>
        ChangedAsync(note, (found, _) => Task.FromResult(Rejected(Note.Edited(found, draft, time.GetUtcNow()))), cancellationToken);

    public ValueTask<Result<NoteRecord, NoteRejection>> AttachAsync(NoteId note, string file, string caption, CancellationToken cancellationToken) =>
        ChangedAsync(
            note,
            async (found, token) =>
            {
                if (!Rejected(Note.Open(found)).TryGetValue(out var open, out var refusal)
                    || !(await shelf.CopyAsync(open, file, token)).TryGetValue(out var media, out refusal))
                {
                    return refusal;
                }

                return Rejected(Note.Attached(open, media, caption, time.GetUtcNow()));
            },
            cancellationToken);

    public ValueTask<Result<NoteRecord, NoteRejection>> ArchiveAsync(NoteId note, CancellationToken cancellationToken) =>
        ChangedAsync(note, (found, _) => Task.FromResult(Rejected(Note.Archived(found, time.GetUtcNow()))), cancellationToken);

    public async ValueTask<IReadOnlyList<NoteRecord>> ListAsync(NotePlace place, CancellationToken cancellationToken)
    {
        var root = await places.RootOfAsync(place, cancellationToken);

        return await serial.RunAsync(
            async token =>
            {
                var notes = await shelf.ReadAsync(root, place, token);

                foreach (var note in notes)
                {
                    known[note.Id] = note;
                }

                return (IReadOnlyList<NoteRecord>)[.. notes.OrderByDescending(note => note.Created)];
            },
            cancellationToken);
    }

    public async ValueTask<Option<NoteRecord>> OpenAsync(NoteId note, CancellationToken cancellationToken) =>
        await serial.RunAsync(
            _ => Task.FromResult(known.TryGetValue(note, out var found) ? Option<NoteRecord>.Some(found) : Option<NoteRecord>.None),
            cancellationToken);

    public ValueTask DisposeAsync() => serial.DisposeAsync();

    private static Result<NoteRecord, NoteRejection> Rejected(Result<NoteRecord, NoteError> note) =>
        note.MapError(error => error switch
        {
            NoteError.UntitledNote => NoteRejection.UntitledNote,
            _ => NoteRejection.Archived,
        });

    private async ValueTask<Result<NoteRecord, NoteRejection>> ChangedAsync(
        NoteId note,
        Func<NoteRecord, CancellationToken, Task<Result<NoteRecord, NoteRejection>>> change,
        CancellationToken cancellationToken) =>
        await serial.RunAsync(
            async token => known.TryGetValue(note, out var found)
                ? await WrittenAsync(await change(found, token), changed => new NoteChanged(changed), token)
                : NoteRejection.UnknownNote,
            cancellationToken);

    private async Task<Result<NoteRecord, NoteRejection>> WrittenAsync<TEvent>(
        Result<NoteRecord, NoteRejection> note,
        Func<NoteRecord, TEvent> told,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        if (!note.TryGetValue(out var changed, out var refusal))
        {
            return refusal;
        }

        var written = await shelf.WriteAsync(changed, cancellationToken);

        foreach (var kept in written.Match<NoteRecord[]>(found => [found], _ => []))
        {
            known[kept.Id] = kept;
            await bus.PublishAsync(told(kept), cancellationToken);
        }

        return written;
    }
}
