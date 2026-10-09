using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Following;

internal sealed record SessionSeen(SessionId Session, ConnectionName Connection, ProviderInfo Provider, Option<AgentAccount> Account);

internal sealed class SessionBook : IHandle<SessionOpened>, IHandle<JobSessionStarted>
{
    private Ledger ledger = new([], [], []);

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        var seen = new SessionSeen(integrationEvent.Session, integrationEvent.Connection, integrationEvent.Provider, integrationEvent.Account);
        var current = Volatile.Read(ref ledger);
        Volatile.Write(ref ledger, current with
        {
            Sessions = current.Sessions.SetItem(seen.Session, seen),
            OnConnection = current.OnConnection.SetItem(seen.Connection, seen.Session),
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref ledger);
        Volatile.Write(ref ledger, current with { OfJob = current.OfJob.SetItem(integrationEvent.Job, integrationEvent.Session) });

        return ValueTask.CompletedTask;
    }

    public Option<SessionSeen> LatestOn(ConnectionName connection)
    {
        var current = Volatile.Read(ref ledger);

        return current.OnConnection.TryGetValue(connection, out var session) ? current.Seen(session) : Option<SessionSeen>.None;
    }

    public Option<SessionSeen> LatestOf(JobId job)
    {
        var current = Volatile.Read(ref ledger);

        return current.OfJob.TryGetValue(job, out var session) ? current.Seen(session) : Option<SessionSeen>.None;
    }

    private sealed record Ledger(
        ImmutableDictionary<SessionId, SessionSeen> Sessions,
        ImmutableDictionary<ConnectionName, SessionId> OnConnection,
        ImmutableDictionary<JobId, SessionId> OfJob)
    {
        public Option<SessionSeen> Seen(SessionId session) =>
            Sessions.TryGetValue(session, out var seen) ? Option<SessionSeen>.Some(seen) : Option<SessionSeen>.None;
    }
}
