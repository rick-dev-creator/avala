using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public interface IAgentSession : IAsyncDisposable
{
    SessionId Id { get; }

    IAsyncEnumerable<IAgentEvent> Events { get; }

    ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken);

    ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken);
}
