using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Contracts;

public interface IAgents
{
    ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken);

    bool IsOpen(SessionId session);

    ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken);

    ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken);

    ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken);
}
