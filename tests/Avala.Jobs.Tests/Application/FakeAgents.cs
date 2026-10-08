using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Application;

internal sealed class FakeAgents : IAgents
{
    private readonly ConcurrentDictionary<SessionId, string> sessions = new();
    private readonly ConcurrentQueue<(SessionId Session, string Message)> sent = new();

    public bool OpeningFails { get; init; }

    public IReadOnlyDictionary<SessionId, string> Sessions => sessions;

    public IReadOnlyList<(SessionId Session, string Message)> Sent => [.. sent];

    public ValueTask<Result<SessionId, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (OpeningFails)
        {
            return ValueTask.FromResult(Result<SessionId, AgentError>.Failure(AgentError.ProviderUnavailable));
        }

        var session = SessionId.New();
        sessions[session] = request.WorkingDirectory;

        return ValueTask.FromResult(Result<SessionId, AgentError>.Success(session));
    }

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken)
    {
        if (!sessions.ContainsKey(session))
        {
            return ValueTask.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.SessionClosed));
        }

        sent.Enqueue((session, message));

        return ValueTask.FromResult(Result<AgentTurn, AgentError>.Success(new AgentTurn(session, TurnId.New())));
    }

    public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(sessions.TryRemove(session, out _)
            ? Result<SessionId, AgentError>.Success(session)
            : Result<SessionId, AgentError>.Failure(AgentError.SessionClosed));
}
