using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
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
    ILogger<AgentSessions> logger) : IAgents, IShutdownTask, IAsyncDisposable
{
    public static readonly TimeSpan InterruptLimit = TimeSpan.FromSeconds(30);

    private ImmutableDictionary<SessionId, LiveSession> live = ImmutableDictionary<SessionId, LiveSession>.Empty;

    public async ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!(await starter.StartAsync(request, cancellationToken)).TryGetValue(out var started, out var error))
        {
            return error;
        }

        var session = started.Session;
        await bus.PublishAsync(
            new SessionOpened(session.Id, started.Provider,request.WorkingDirectory, started.Connection) { Account = session.Account, ProcessTree = started.Tree, Capabilities = started.Capabilities },
            cancellationToken);
        ImmutableInterlocked.TryAdd(ref live, session.Id, new LiveSession(session, started.Capabilities, PumpAsync));

        return new OpenedSession(session.Id, started.Resumed, started.Connection) { Capabilities = started.Capabilities };
    }

    public bool IsOpen(SessionId session) => Volatile.Read(ref live).TryGetValue(session, out var running) && !running.Ended;

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) =>
        SayAsync(session, new UserTurn(message), cancellationToken);

    public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) =>
        SayAsync(session, new UserTurn(message) { MidTurn = true }, cancellationToken);

    public async ValueTask<Result<AgentTurn, AgentError>> SayAsync(SessionId session, UserTurn turn, CancellationToken cancellationToken) =>
        !Volatile.Read(ref live).TryGetValue(session, out var running) ? AgentError.SessionClosed
        : turn.MidTurn && !running.Capabilities.Has<AcceptsMessagesMidTurn>() ? AgentError.Unsupported
        : !turn.Attachments.All(attachment => running.Capabilities.Get<AcceptsAttachments>().Match(accepted => accepted.Accepts(attachment.MediaType), () => false)) ? AgentError.UnacceptedAttachment
        : (await running.Session.SendAsync(turn, cancellationToken)).Map(sent => new AgentTurn(session, sent));

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
        : !running.Capabilities.Has<AsksForms>() ? AgentError.Unsupported
        : await running.OpenForm(answer.Item).Match(
            async form => form.Accepts(answer)
                ? await running.Session.AnswerAsync(answer, cancellationToken)
                : Result<ItemId, AgentError>.Failure(AgentError.InvalidAnswer),
            () => Task.FromResult(Result<ItemId, AgentError>.Failure(AgentError.NoPendingForm)));

    public async ValueTask<Result<ItemId, AgentError>> ReturnAsync(
        SessionId session,
        ToolResult result,
        CancellationToken cancellationToken) =>
        !Volatile.Read(ref live).TryGetValue(session, out var running) ? AgentError.SessionClosed
        : !running.Capabilities.Get<AcceptsTools>().Match(accepted => accepted.Accepts(ToolSurface.Executed), () => false) ? AgentError.Unsupported
        : !running.AwaitsResult(result.Item) ? AgentError.NoPendingCall
        : await running.Session.ReturnAsync(result, cancellationToken);

    public async ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) =>
        !Volatile.Read(ref live).TryGetValue(session, out var running) ? AgentError.SessionClosed
        : !running.Capabilities.Has<Interruptible>() ? AgentError.Unsupported
        : await ConfirmedAsync(running, cancellationToken);

    public async ValueTask<Result<RemoteState, AgentError>> ControlRemotelyAsync(SessionId session, bool on, CancellationToken cancellationToken) =>
        !Volatile.Read(ref live).TryGetValue(session, out var running) ? AgentError.SessionClosed
        : !running.Capabilities.Has<ControlsRemotely>() ? AgentError.NotRemoteControllable
        : await running.Session.ControlRemotelyAsync(on, cancellationToken);

    public async ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken)
    {
        if (!ImmutableInterlocked.TryRemove(ref live, session, out var running))
        {
            return AgentError.SessionClosed;
        }

        await running.DisposeAsync();
        await bus.PublishAsync(new SessionStopped(session), cancellationToken);

        return session;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var session in Volatile.Read(ref live).Keys)
        {
            await StopAsync(session, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private async Task<Result<TurnId, AgentError>> ConfirmedAsync(LiveSession running, CancellationToken cancellationToken)
    {
        using var limit = new CancellationTokenSource(InterruptLimit, clock);

        try
        {
            return await running.Session.InterruptAsync(cancellationToken).AsTask().WaitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return AgentError.InterruptUnconfirmed;
        }
    }

    private async Task PumpAsync(LiveSession running, CancellationToken cancellationToken)
    {
        var session = running.Session;
        Turn? turn = null;
        var ending = SessionEnding.Closed;
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["Session"] = session.Id.Value });

        try
        {
            await foreach (var reported in session.Events.WithCancellation(cancellationToken))
            {
                var agentEvent = running.Priced(reported);

                if (agentEvent is RemoteControlChanged changed)
                {
                    await bus.PublishAsync(new RemoteControlReported(changed.Session, changed.Control), cancellationToken);
                    continue;
                }

                var applied = agentEvent is TurnStarted started
                    ? Turn.Begin(started).Map(begun =>
                    {
                        turn = begun;
                        return new TurnProgress([started]);
                    })
                    : turn?.Apply(agentEvent, clock.GetUtcNow()) ?? Result<TurnProgress, TurnError>.Failure(TurnError.ForeignEvent);

                await ForwardAsync(agentEvent, applied, running, turn?.Limit ?? Option<UsageLimit>.None, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseStoppedAsync(turn, running);

            return;
        }
        catch (Exception exception)
        {
            LogSessionFailed(session.Id.Value, exception);
            ending = SessionEnding.Crashed;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await CloseStoppedAsync(turn, running);

            return;
        }

        await bus.PublishAsync(new SessionEnded(session.Id, ending), CancellationToken.None);

        if (turn is { IsLive: true })
        {
            var failed = new TurnCompleted(turn.Session, turn.Id, TurnOutcome.Failed);
            await ForwardAsync(failed, turn.Apply(failed, clock.GetUtcNow()), running, Option<UsageLimit>.None, CancellationToken.None);
        }
    }

    private async Task CloseStoppedAsync(Turn? turn, LiveSession running)
    {
        if (turn is not { IsLive: true })
        {
            return;
        }

        var stopped = new TurnCompleted(turn.Session, turn.Id, TurnOutcome.Interrupted);

        if (turn.Apply(stopped, clock.GetUtcNow()).TryGetValue(out var progress, out _))
        {
            foreach (var forwarded in progress.Events)
            {
                running.Track(forwarded);
                await bus.PublishAsync(new AgentActivity(forwarded), CancellationToken.None);
            }
        }
    }

    private async Task ForwardAsync(
        IAgentEvent agentEvent,
        Result<TurnProgress, TurnError> applied,
        LiveSession running,
        Option<UsageLimit> limit,
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
                    await bus.PublishAsync(
                        new TurnFinished(completed.Session, completed.Turn, completed.Outcome)
                        {
                            Limit = completed.Outcome == TurnOutcome.LimitReached ? limit : Option<UsageLimit>.None,
                        },
                        cancellationToken);
                    break;
                case RemoteMessageArrived arrived:
                    await bus.PublishAsync(new RemoteMessageReceived(arrived.Session, arrived.Turn, arrived.Text) { BeganTurn = arrived.BeganTurn }, cancellationToken);
                    break;
                case ResumeTokenIssued issued when running.Capabilities.Has<Resumable>():
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
