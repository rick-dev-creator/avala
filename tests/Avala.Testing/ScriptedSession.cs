using System.Collections.Concurrent;
using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Testing;

public sealed class ScriptedSession(Func<SessionId, TurnId, IEnumerable<IAgentEvent>> script, SessionOptions options) : IAgentSession
{
    private readonly Channel<IAgentEvent> events = Channel.CreateUnbounded<IAgentEvent>();
    private readonly ConcurrentQueue<string> received = new();
    private readonly ConcurrentQueue<PermissionDecision> decisions = new();

    public SessionId Id { get; } = SessionId.New();

    public SessionOptions Options { get; } = options;

    public IReadOnlyList<string> Received => [.. received];

    public IReadOnlyList<PermissionDecision> Decisions => [.. decisions];

    public bool IsDisposed { get; private set; }

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken)
    {
        var id = TurnId.New();
        received.Enqueue(turn.Text);

        foreach (var agentEvent in script(Id, id))
        {
            await events.Writer.WriteAsync(agentEvent, cancellationToken);
        }

        return id;
    }

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken)
    {
        decisions.Enqueue(decision);

        return ValueTask.FromResult(Result<ItemId, AgentError>.Success(decision.Item));
    }

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<TurnId, AgentError>.Failure(AgentError.NoTurnInProgress));

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        events.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }
}
