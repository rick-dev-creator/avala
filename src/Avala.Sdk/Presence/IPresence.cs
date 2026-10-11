namespace Avala.Sdk.Presence;

public interface IPresence
{
    DateTimeOffset LastSeen { get; }
}

public static class Attention
{
    extension(IPresence presence)
    {
        public bool IsAwayFor(TimeSpan span, DateTimeOffset now) => now - presence.LastSeen >= span;
    }
}
