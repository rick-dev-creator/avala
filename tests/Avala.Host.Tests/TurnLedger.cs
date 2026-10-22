using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class TurnLedger : IPlugin, IHandle<AgentActivity>, IHandle<SessionEnded>, IHandle<SessionStopped>, IHandle<JobSessionRested>
{
    private ImmutableHashSet<OpenTurn> open = [];
    private ImmutableHashSet<OpenTurn> finished = [];
    private ImmutableHashSet<OpenItem> items = [];
    private ImmutableHashSet<OpenItem> backgrounded = [];
    private ImmutableHashSet<SessionId> ended = [];
    private ImmutableList<string> restedWhileWorking = [];

    public PluginInfo Info { get; } = new("turn-ledger", "Turn ledger");

    public IReadOnlyList<OpenTurn> OpenAfterTheirSession
    {
        get
        {
            var gone = Volatile.Read(ref ended);

            return [.. Volatile.Read(ref open).Where(turn => gone.Contains(turn.Session))];
        }
    }

    public IReadOnlyList<OpenItem> OpenAfterTheirTurn
    {
        get
        {
            var done = Volatile.Read(ref finished);

            return [.. Volatile.Read(ref items).Where(item => done.Contains(new OpenTurn(item.Session, item.Turn)))];
        }
    }

    public IReadOnlyList<string> RestedWhileWorking => Volatile.Read(ref restedWhileWorking);

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.AddSingleton(this);
        registrar.Services.AddSingleton<IHandle<AgentActivity>>(this);
        registrar.Services.AddSingleton<IHandle<SessionEnded>>(this);
        registrar.Services.AddSingleton<IHandle<SessionStopped>>(this);
        registrar.Services.AddSingleton<IHandle<JobSessionRested>>(this);
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        _ = integrationEvent.Event switch
        {
            TurnStarted started => ImmutableInterlocked.Update(ref open, turns => turns.Add(new OpenTurn(started.Session, started.Turn))),
            TurnCompleted completed => Finish(new OpenTurn(completed.Session, completed.Turn)),
            ItemStarted started => ImmutableInterlocked.Update(ref items, opened => opened.Add(new OpenItem(started.Session, started.Turn, started.Item))),
            ItemBackgrounded moved => ImmutableInterlocked.Update(ref backgrounded, working => working.Add(new OpenItem(moved.Session, moved.Turn, moved.Item))),
            ItemCompleted completed => Close(new OpenItem(completed.Session, completed.Turn, completed.Item)),
            _ => false,
        };

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) => EndAsync(integrationEvent.Session);

    public ValueTask HandleAsync(SessionStopped integrationEvent, CancellationToken cancellationToken) => EndAsync(integrationEvent.Session);

    public ValueTask HandleAsync(JobSessionRested integrationEvent, CancellationToken cancellationToken)
    {
        var session = integrationEvent.Session;
        var working = Volatile.Read(ref backgrounded).Where(item => item.Session == session).Select(item => item.Item.Value).ToList();
        var live = Volatile.Read(ref open).Any(turn => turn.Session == session);

        if (working.Count > 0 || live)
        {
            _ = ImmutableInterlocked.Update(ref restedWhileWorking, rests => rests.Add(
                $"A session with work pending is never rested: session {session.Value} of job {integrationEvent.Job.Value} was rested {(live ? "during a live turn" : "with background work")}{(working.Count > 0 ? $" ({string.Join(", ", working)})" : string.Empty)}."));
        }

        return ValueTask.CompletedTask;
    }

    private bool Finish(OpenTurn turn)
    {
        _ = ImmutableInterlocked.Update(ref open, turns => turns.Remove(turn));

        return ImmutableInterlocked.Update(ref finished, turns => turns.Add(turn));
    }

    private bool Close(OpenItem item)
    {
        _ = ImmutableInterlocked.Update(ref backgrounded, working => working.Remove(item));

        return ImmutableInterlocked.Update(ref items, opened => opened.Remove(item));
    }

    private ValueTask EndAsync(SessionId session)
    {
        _ = ImmutableInterlocked.Update(ref ended, sessions => sessions.Add(session));

        return ValueTask.CompletedTask;
    }
}

internal sealed record OpenTurn(SessionId Session, TurnId Turn);

internal sealed record OpenItem(SessionId Session, TurnId Turn, ItemId Item);
