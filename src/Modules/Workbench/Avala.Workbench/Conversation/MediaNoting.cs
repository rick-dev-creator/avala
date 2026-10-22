using Avala.Agents.Contracts.Sessions;
using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class MediaNoting(INotes notes, IJobCatalog catalog)
{
    public async Task<bool> AddAsync(JobId job, Attachment media, string caption, CancellationToken cancellationToken)
    {
        var history = await catalog.HistoryAsync(job, cancellationToken);

        if (history.Match<string?>(found => found.Summary.Repository, () => null) is not { } repository)
        {
            return false;
        }

        var place = NotePlace.OfJob(job, repository);
        var open = (await notes.ListAsync(place, cancellationToken))
            .Where(note => note.State == NoteState.Kept)
            .OrderByDescending(note => note.Updated)
            .Select(note => Option<NoteRecord>.Some(note))
            .FirstOrDefault();
        var target = await open.Match(
            note => ValueTask.FromResult(Result<NoteRecord, NoteRejection>.Success(note)),
            () => notes.TakeAsync(place, new NoteDraft(caption.Length > 0 ? caption : media.Name, string.Empty), cancellationToken));

        return target.TryGetValue(out var note, out _)
            && (await notes.AttachAsync(note.Id, media.Path, caption, cancellationToken)).TryGetValue(out _, out _);
    }
}

internal sealed class MediaActions(IClipboard clipboard, MediaNoting noting)
{
    public IClipboard Clipboard => clipboard;

    public MediaNoting Noting => noting;

    public ITimelineItem Show(JobId job, MediaEntry entry, IPictureDecoder pictures) => new MediaViewModel(job, entry, pictures, this);
}
