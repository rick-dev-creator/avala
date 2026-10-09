using System.Collections.Concurrent;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Capturing;
using Avala.Recording.Recordings;

namespace Avala.Recording.Tests.Capturing;

internal sealed class MemoryStore : IRecordingStore
{
    private readonly ConcurrentDictionary<SessionId, SessionRecording> recordings = new();

    public IReadOnlyCollection<SessionRecording> Recordings => [.. recordings.Values];

    public SessionRecording Of(SessionId session) => recordings[session];

    public void Begin(SessionId session, RecordingHeader header, RecordingSettings settings) =>
        recordings[session] = SessionRecording.Begin(header, settings);

    public void Note(SessionId session, TimeSpan at, IRecordedFact fact) =>
        recordings.AddOrUpdate(session, _ => throw new InvalidOperationException("Noted before it began."), (_, recording) => recording.Add(at, fact));
}
