using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Tests.Conformance;

internal sealed class ScriptedSession(Func<SessionId, TurnId, IEnumerable<IAgentEvent>> script) : IAgentSession
{
    private readonly Channel<IAgentEvent> events = Channel.CreateUnbounded<IAgentEvent>();

    public SessionId Id { get; } = SessionId.New();

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken)
    {
        var id = TurnId.New();

        foreach (var agentEvent in script(Id, id))
        {
            await events.Writer.WriteAsync(agentEvent, cancellationToken);
        }

        return id;
    }

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ItemId, AgentError>.Failure(AgentError.NoPendingPermission));

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<TurnId, AgentError>.Failure(AgentError.NoTurnInProgress));

    public ValueTask DisposeAsync()
    {
        events.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }
}
