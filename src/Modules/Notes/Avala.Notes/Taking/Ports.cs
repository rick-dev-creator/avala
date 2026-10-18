using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;

namespace Avala.Notes.Taking;

internal interface INoteShelf
{
    Task<Result<NoteRecord, NoteRejection>> WriteAsync(NoteRecord note, CancellationToken cancellationToken);

    Task<Result<NoteMedia, NoteRejection>> CopyAsync(NoteRecord note, string file, CancellationToken cancellationToken);

    Task<IReadOnlyList<NoteRecord>> ReadAsync(string root, NotePlace place, CancellationToken cancellationToken);

    IReadOnlyList<JobId> JobsAt(string root);

    Task<bool> KeepAsync(string from, string to, string folder, CancellationToken cancellationToken);
}
