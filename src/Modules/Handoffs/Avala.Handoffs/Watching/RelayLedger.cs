using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Handoffs.Watching;

internal sealed class RelayLedger
{
    private ImmutableHashSet<SessionId> settled = [];
    private ImmutableHashSet<SessionId> winding = [];

    public bool IsSettled(SessionId session) => Volatile.Read(ref settled).Contains(session);

    public bool IsWinding(SessionId session) => Volatile.Read(ref winding).Contains(session);

    public void Settle(SessionId session)
    {
        ImmutableInterlocked.Update(ref settled, known => known.Add(session));
        ImmutableInterlocked.Update(ref winding, known => known.Remove(session));
    }

    public void Wind(SessionId session)
    {
        ImmutableInterlocked.Update(ref settled, known => known.Add(session));
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
