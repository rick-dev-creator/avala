using Avala.Sdk.Presence;

namespace Avala.Runtime.Presence;

internal sealed class WindowPresence(TimeProvider clock) : IPresence
{
    private long lastSeen = clock.GetUtcNow().UtcTicks;

    public DateTimeOffset LastSeen => new(Volatile.Read(ref lastSeen), TimeSpan.Zero);

    public void Seen() => Volatile.Write(ref lastSeen, clock.GetUtcNow().UtcTicks);
}
