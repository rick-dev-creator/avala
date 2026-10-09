using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace Avala.Agents.Sessions;

internal sealed partial class AgentSessions(
    IEnumerable<IAgentProvider> providers,
    IEventBus bus,
    TimeProvider clock,
    ILogger<AgentSessions> logger) : IAgents, IAsyncDisposable
{
    private ImmutableDictionary<SessionId, LiveSession> live = ImmutableDictionary<SessionId, LiveSession>.Empty;

    public async ValueTask<Result<SessionId, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (providers.FirstOrDefault() is not { } provider)
        {
            return AgentError.ProviderUnavailable;
        }

        var options = new SessionOptions(request.WorkingDirectory, PermissionMode.AllowEdits);

        if (!(await provider.StartAsync(options, cancellationToken)).TryGetValue(out var session, out var error))
        {
            return error;
        }

        await bus.PublishAsync(new SessionOpened(session.Id, provider.Info, request.WorkingDirectory), cancellationToken);
        ImmutableInterlocked.TryAdd(ref live, session.Id, new LiveSession(session, provider.Capabilities, PumpAsync));

        return session.Id;
    }

    public async ValueTask<Result<AgentTurn, AgentError>> SendAsync(
        SessionId session,
        string message,
        CancellationToken cancellationToken) =>
        Volatile.Read(ref live).TryGetValue(session, out var running)
            ? (await running.Session.SendAsync(new UserTurn(message), cancellationToken)).Map(turn => new AgentTurn(session, turn))
            : AgentError.SessionClosed;

    public async ValueTask<Result<ItemId, AgentError>> RespondAsync(
        SessionId session,
        PermissionDecision decision,
        CancellationToken cancellationToken) =>
        Volatile.Read(ref live).TryGetValue(session, out var running)
            ? await running.Session.RespondAsync(decision, cancellationToken)
            : AgentError.SessionClosed;

    public async ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) =>
        !Volatile.Read(ref live).TryGetValue(session, out var running) ? AgentError.SessionClosed
        : !running.Capabilities.CanInterrupt ? AgentError.Unsupported
        : await running.Session.InterruptAsync(cancellationToken);

    public async ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken)
    {
        if (!ImmutableInterlocked.TryRemove(ref live, session, out var running))
        {
            return AgentError.SessionClosed;
        }

        await running.DisposeAsync();

        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in Volatile.Read(ref live).Keys)
        {
            await StopAsync(session, CancellationToken.None);
        }
    }

    private async Task PumpAsync(IAgentSession session, CancellationToken cancellationToken)
    {
        Turn? turn = null;
        var ending = SessionEnding.Closed;

        try
        {
            await foreach (var agentEvent in session.Events.WithCancellation(cancellationToken))
            {
                var applied = agentEvent is TurnStarted started
                    ? Turn.Begin(started).Map(begun =>
                    {
                        turn = begun;
                        return new TurnProgress([started]);
                    })
                    : turn?.Apply(agentEvent, clock.GetUtcNow()) ?? Result<TurnProgress, TurnError>.Failure(TurnError.ForeignEvent);

                await ForwardAsync(agentEvent, applied, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogSessionFailed(session.Id.Value, exception);
            ending = SessionEnding.Crashed;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await bus.PublishAsync(new SessionEnded(session.Id, ending), CancellationToken.None);

        if (turn is { IsLive: true })
        {
            var failed = new TurnCompleted(turn.Session, turn.Id, TurnOutcome.Failed);
            await ForwardAsync(failed, turn.Apply(failed, clock.GetUtcNow()), CancellationToken.None);
        }
    }

    private async Task ForwardAsync(IAgentEvent agentEvent, Result<TurnProgress, TurnError> applied, CancellationToken cancellationToken)
    {
        if (!applied.TryGetValue(out var progress, out var rejection))
        {
            LogRejected(agentEvent.GetType().Name, rejection);
            return;
        }

        foreach (var forwarded in progress.Events)
        {
            await bus.PublishAsync(new AgentActivity(forwarded), cancellationToken);

            if (forwarded is TurnCompleted completed)
            {
                await bus.PublishAsync(new TurnFinished(completed.Session, completed.Turn, completed.Outcome), cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected {Event} from an agent: {Rejection}")]
    private partial void LogRejected(string @event, TurnError rejection);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent session {Session} failed")]
    private partial void LogSessionFailed(Guid session, Exception exception);
}
