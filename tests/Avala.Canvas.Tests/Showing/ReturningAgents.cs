using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Canvas.Tests.Showing;

internal sealed class ReturningAgents : IAgents
{
    private readonly ConcurrentQueue<(SessionId Session, ToolResult Result)> results = new();

    public IReadOnlyList<(SessionId Session, ToolResult Result)> Results => [.. results];

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken)
    {
        results.Enqueue((session, result));

        return ValueTask.FromResult(Result<ItemId, AgentError>.Success(result.Item));
    }

    public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsOpen(SessionId session) => true;

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<RemoteState, AgentError>> ControlRemotelyAsync(SessionId session, bool on, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<AgentTurn, AgentError>> SayAsync(SessionId session, UserTurn turn, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();
}
