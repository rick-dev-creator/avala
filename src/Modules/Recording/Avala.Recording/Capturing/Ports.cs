using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Sdk;

namespace Avala.Recording.Capturing;

internal interface IRecordingSettings
{
    ValueTask<RecordingSettings> LoadAsync(CancellationToken cancellationToken);
}

internal interface IRecordingStore
{
    void Begin(SessionId session, RecordingHeader header, RecordingSettings settings);

    void Note(SessionId session, TimeSpan at, IRecordedFact fact);
}

internal interface IEditedFiles
{
    ValueTask<Option<string>> ReadAsync(string path, CancellationToken cancellationToken);

    Task<FolderStamps> StampAsync(string folder, CancellationToken cancellationToken);
}

internal readonly record struct FileStamp(long Length, DateTime Written);

internal sealed record FolderStamps(IReadOnlyDictionary<string, FileStamp> Files)
{
    public IEnumerable<string> ChangedSince(FolderStamps before) =>
        Files.Where(file => !before.Files.TryGetValue(file.Key, out var stamp) || stamp != file.Value).Select(file => file.Key).Order(StringComparer.Ordinal);
}
