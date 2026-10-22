using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class TurnLedger : IPlugin, IHandle<AgentActivity>, IHandle<SessionEnded>, IHandle<SessionStopped>
{
    private ImmutableHashSet<OpenTurn> open = [];
    private ImmutableHashSet<SessionId> ended = [];

    public PluginInfo Info { get; } = new("turn-ledger", "Turn ledger");

    public IReadOnlyList<OpenTurn> OpenAfterTheirSession
    {
        get
        {
            var gone = Volatile.Read(ref ended);

            return [.. Volatile.Read(ref open).Where(turn => gone.Contains(turn.Session))];
        }
    }

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.AddSingleton(this);
        registrar.Services.AddSingleton<IHandle<AgentActivity>>(this);
        registrar.Services.AddSingleton<IHandle<SessionEnded>>(this);
        registrar.Services.AddSingleton<IHandle<SessionStopped>>(this);
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        _ = integrationEvent.Event switch
        {
            TurnStarted started => ImmutableInterlocked.Update(ref open, turns => turns.Add(new OpenTurn(started.Session, started.Turn))),
            TurnCompleted completed => ImmutableInterlocked.Update(ref open, turns => turns.Remove(new OpenTurn(completed.Session, completed.Turn))),
            _ => false,
        };

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken) => EndAsync(integrationEvent.Session);

    public ValueTask HandleAsync(SessionStopped integrationEvent, CancellationToken cancellationToken) => EndAsync(integrationEvent.Session);

    private ValueTask EndAsync(SessionId session)
    {
        _ = ImmutableInterlocked.Update(ref ended, sessions => sessions.Add(session));

        return ValueTask.CompletedTask;
    }
}

internal sealed record OpenTurn(SessionId Session, TurnId Turn);
