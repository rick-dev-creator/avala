using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Notes.Contracts;

public interface INotes
{
    ValueTask<Result<NoteRecord, NoteRejection>> TakeAsync(NotePlace place, NoteDraft draft, CancellationToken cancellationToken);

    ValueTask<Result<NoteRecord, NoteRejection>> EditAsync(NoteId note, NoteDraft draft, CancellationToken cancellationToken);

    ValueTask<Result<NoteRecord, NoteRejection>> AttachAsync(NoteId note, string file, string caption, CancellationToken cancellationToken);

    ValueTask<Result<NoteRecord, NoteRejection>> MakeIdeaAsync(NoteId note, CancellationToken cancellationToken);

    ValueTask<Result<NoteRecord, NoteRejection>> ArchiveAsync(NoteId note, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<NoteRecord>> ListAsync(NotePlace place, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<NoteRecord>> SearchAsync(string query, CancellationToken cancellationToken);

    ValueTask<Option<NoteRecord>> OpenAsync(NoteId note, CancellationToken cancellationToken);
}

public interface IIdeas
{
    ValueTask<Result<JobId, IdeaRejection>> StartJobAsync(NoteId idea, CancellationToken cancellationToken);

    ValueTask<Result<Uri, IdeaRejection>> PublishIssueAsync(NoteId idea, CancellationToken cancellationToken);
}

public enum IdeaRejection
{
    UnknownNote,
    NotAnIdea,
    JobRefused,
    IssueRefused,
}

public readonly record struct NoteId(Guid Value)
{
    public static NoteId New() => new(Guid.CreateVersion7());
}

public sealed record NotePlace(string Repository)
{
    public Option<JobId> Job { get; init; }

    public static NotePlace OfJob(JobId job, string repository) => new(repository) { Job = job };

    public static NotePlace OfProject(string repository) => new(repository);
}

public enum NoteState
{
    Kept,
    Idea,
    Archived,
}

public sealed record NoteDraft(string Title, string Body);

public sealed record NoteMedia(string File, string MediaType);

public sealed record NoteRecord(
    NoteId Id,
    NotePlace Place,
    string Title,
    string Body,
    IReadOnlyList<NoteMedia> Media,
    NoteState State,
    DateTimeOffset Created,
    DateTimeOffset Updated)
{
    public string Folder { get; init; } = string.Empty;

    public string Root { get; init; } = string.Empty;
}

public enum NoteRejection
{
    UnknownNote,
    UntitledNote,
    Archived,
    NotAnImage,
    MissingMedia,
    MediaTooLarge,
    Unwritable,
}

public sealed record NoteTaken(NoteRecord Note) : IIntegrationEvent;

public sealed record NoteChanged(NoteRecord Note) : IIntegrationEvent;
