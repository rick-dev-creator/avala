using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Handoffs.Watching;

internal sealed class RelayLedger
{
    private ImmutableDictionary<SessionId, Option<DateTimeOffset>> settled = ImmutableDictionary<SessionId, Option<DateTimeOffset>>.Empty;
    private ImmutableHashSet<SessionId> winding = [];

    public bool IsSettled(SessionId session, DateTimeOffset now) =>
        Volatile.Read(ref settled).TryGetValue(session, out var until) && until.Match(resets => now < resets, () => true);

    public bool IsWinding(SessionId session) => Volatile.Read(ref winding).Contains(session);

    public void Settle(SessionId session, Option<DateTimeOffset> until)
    {
        ImmutableInterlocked.Update(ref settled, known => known.SetItem(session, until));
        ImmutableInterlocked.Update(ref winding, known => known.Remove(session));
    }

    public void Wind(SessionId session, Option<DateTimeOffset> until)
    {
        ImmutableInterlocked.Update(ref settled, known => known.SetItem(session, until));
        ImmutableInterlocked.Update(ref winding, known => known.Add(session));
    }

    public bool Unwind(SessionId session)
    {
        var before = Volatile.Read(ref winding);

        while (before.Contains(session))
        {
            var seen = Interlocked.CompareExchange(ref winding, before.Remove(session), before);

            if (ReferenceEquals(seen, before))
            {
                return true;
            }

            before = seen;
        }

        return false;
    }
}
