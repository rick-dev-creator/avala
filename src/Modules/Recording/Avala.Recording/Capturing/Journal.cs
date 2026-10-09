using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;

namespace Avala.Recording.Capturing;

internal sealed class Journal(SessionId session, IRecordingStore store, TimeProvider clock)
{
    private readonly long started = clock.GetTimestamp();
    private int inputs;

    public int NextInput() => Interlocked.Increment(ref inputs);

    public void Note(IRecordedFact fact) => store.Note(session, clock.GetElapsedTime(started), fact);
}
