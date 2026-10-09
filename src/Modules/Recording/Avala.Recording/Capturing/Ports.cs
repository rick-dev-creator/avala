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
}
