using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public interface IAgentSession : IAsyncDisposable
{
    SessionId Id { get; }

    Option<AgentAccount> Account { get; }

    IReadOnlyList<string> OwnFolders => [];

    IReadOnlyList<string> WritableFolders => [];

    IAsyncEnumerable<IAgentEvent> Events { get; }

    ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken);

    ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken);

    ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken);

    ValueTask<Result<RemoteState, AgentError>> ControlRemotelyAsync(bool on, CancellationToken cancellationToken);
}
