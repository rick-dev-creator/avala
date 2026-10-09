using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class FakeAgents : IAgents
{
    private readonly ConcurrentDictionary<SessionId, string> sessions = new();
    private readonly ConcurrentQueue<(SessionId Session, string Message)> sent = new();
    private readonly ConcurrentQueue<SessionId> interrupted = new();
    private readonly ConcurrentQueue<SessionId> stopped = new();

    private readonly ConcurrentQueue<AgentRequest> requests = new();

    public static ConnectionName DefaultConnection { get; } = new("default");

    public bool OpeningFails { get; set; }

    public ISet<string> UnknownConnections { get; } = new HashSet<string>(StringComparer.Ordinal);

    public bool Resumes { get; init; }

    public Option<AgentError> InterruptRejection { get; init; }

    public IReadOnlyList<AgentRequest> Requests => [.. requests];

    public IReadOnlyList<SessionId> Interrupted => [.. interrupted];

    public IReadOnlyList<SessionId> Stopped => [.. stopped];

    public IReadOnlyDictionary<SessionId, string> Sessions => sessions;

    public IReadOnlyList<(SessionId Session, string Message)> Sent => [.. sent];

    public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request);

        if (OpeningFails)
        {
            return ValueTask.FromResult(Result<OpenedSession, AgentError>.Failure(AgentError.ProviderUnavailable));
        }

        var connection = request.Connection.Match(named => named, () => DefaultConnection);

        if (UnknownConnections.Contains(connection.Value))
        {
            return ValueTask.FromResult(Result<OpenedSession, AgentError>.Failure(AgentError.UnknownConnection));
        }

        var session = SessionId.New();
        sessions[session] = request.WorkingDirectory;

        return ValueTask.FromResult(Result<OpenedSession, AgentError>.Success(new OpenedSession(session, Resumes && request.Resume.IsSome, connection)));
    }

    public bool IsOpen(SessionId session) => sessions.ContainsKey(session);

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken)
    {
        if (!sessions.ContainsKey(session))
        {
            return ValueTask.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.SessionClosed));
        }

        sent.Enqueue((session, message));

        return ValueTask.FromResult(Result<AgentTurn, AgentError>.Success(new AgentTurn(session, TurnId.New())));
    }

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(
        SessionId session,
        PermissionDecision decision,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(sessions.ContainsKey(session)
            ? Result<ItemId, AgentError>.Success(decision.Item)
            : Result<ItemId, AgentError>.Failure(AgentError.SessionClosed));

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ItemId, AgentError>.Failure(AgentError.Unsupported));

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ItemId, AgentError>.Failure(AgentError.Unsupported));

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken)
    {
        if (!sessions.ContainsKey(session))
        {
            return ValueTask.FromResult(Result<TurnId, AgentError>.Failure(AgentError.SessionClosed));
        }

        interrupted.Enqueue(session);

        return ValueTask.FromResult(InterruptRejection.Match(
            Result<TurnId, AgentError>.Failure,
            () => Result<TurnId, AgentError>.Success(TurnId.New())));
    }

    public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken)
    {
        if (!sessions.TryRemove(session, out _))
        {
            return ValueTask.FromResult(Result<SessionId, AgentError>.Failure(AgentError.SessionClosed));
        }

        stopped.Enqueue(session);

        return ValueTask.FromResult(Result<SessionId, AgentError>.Success(session));
    }
}
