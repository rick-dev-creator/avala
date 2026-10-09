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
    SessionStarter starter,
    IEventBus bus,
    TimeProvider clock,
    ILogger<AgentSessions> logger) : IAgents, IAsyncDisposable
{
    private ImmutableDictionary<SessionId, LiveSession> live = ImmutableDictionary<SessionId, LiveSession>.Empty;

    public async ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!(await starter.StartAsync(request, cancellationToken)).TryGetValue(out var started, out var error))
        {
            return error;
        }

        var session = started.Session;
        await bus.PublishAsync(
            new SessionOpened(session.Id, started.Provider.Info, request.WorkingDirectory) { Account = session.Account },
            cancellationToken);
        ImmutableInterlocked.TryAdd(ref live, session.Id, new LiveSession(session, started.Provider.Capabilities, PumpAsync));

        return new OpenedSession(session.Id, started.Resumed);
    }

    public bool IsOpen(SessionId session) => Volatile.Read(ref live).TryGetValue(session, out var running) && !running.Ended;

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

    public async ValueTask<Result<ItemId, AgentError>> AnswerAsync(
        SessionId session,
        FormAnswer answer,
        CancellationToken cancellationToken) =>
        !Volatile.Read(ref live).TryGetValue(session, out var running) ? AgentError.SessionClosed
        : !running.Capabilities.AsksQuestions ? AgentError.Unsupported
        : await running.OpenForm(answer.Item).Match(
            async form => form.Accepts(answer)
                ? await running.Session.AnswerAsync(answer, cancellationToken)
                : Result<ItemId, AgentError>.Failure(AgentError.InvalidAnswer),
            () => Task.FromResult(Result<ItemId, AgentError>.Failure(AgentError.NoPendingForm)));

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

    private async Task PumpAsync(LiveSession running, CancellationToken cancellationToken)
    {
        var session = running.Session;
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

                await ForwardAsync(agentEvent, applied, running, cancellationToken);
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
            await ForwardAsync(failed, turn.Apply(failed, clock.GetUtcNow()), running, CancellationToken.None);
        }
    }

    private async Task ForwardAsync(
        IAgentEvent agentEvent,
        Result<TurnProgress, TurnError> applied,
        LiveSession running,
        CancellationToken cancellationToken)
    {
        if (!applied.TryGetValue(out var progress, out var rejection))
        {
            LogRejected(agentEvent.GetType().Name, rejection);
            return;
        }

        foreach (var forwarded in progress.Events)
        {
            running.Track(forwarded);
            await bus.PublishAsync(new AgentActivity(forwarded), cancellationToken);

            switch (forwarded)
            {
                case TurnCompleted completed:
                    await bus.PublishAsync(new TurnFinished(completed.Session, completed.Turn, completed.Outcome), cancellationToken);
                    break;
                case ResumeTokenIssued issued when running.Capabilities.CanResume:
                    await bus.PublishAsync(new SessionResumable(issued.Session, issued.Token), cancellationToken);
                    break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected {Event} from an agent: {Rejection}")]
    private partial void LogRejected(string @event, TurnError rejection);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent session {Session} failed")]
    private partial void LogSessionFailed(Guid session, Exception exception);
}
