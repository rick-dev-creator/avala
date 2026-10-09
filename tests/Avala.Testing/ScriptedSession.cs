using System.Collections.Concurrent;
using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Testing;

public sealed class ScriptedSession(
    Func<SessionId, TurnId, IEnumerable<IAgentEvent>> script,
    SessionOptions options,
    Func<Option<AgentAccount>> account) : IAgentSession
{
    private readonly Channel<IAgentEvent> events = Channel.CreateUnbounded<IAgentEvent>();
    private readonly ConcurrentQueue<string> received = new();
    private readonly ConcurrentQueue<PermissionDecision> decisions = new();
    private readonly ConcurrentQueue<TurnId> turns = new();
    private readonly ConcurrentQueue<FormAnswer> answers = new();
    private readonly ConcurrentQueue<ToolResult> results = new();
    private int interruptions;

    public SessionId Id { get; } = SessionId.New();

    public SessionOptions Options { get; } = options;

    public Option<AgentAccount> Account => account();

    public IReadOnlyList<string> Received => [.. received];

    public IReadOnlyList<PermissionDecision> Decisions => [.. decisions];

    public IReadOnlyList<FormAnswer> Answers => [.. answers];

    public IReadOnlyList<ToolResult> Results => [.. results];

    public int Interruptions => interruptions;

    public bool IsDisposed { get; private set; }

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken)
    {
        var id = TurnId.New();
        received.Enqueue(turn.Text);
        turns.Enqueue(id);

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

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken)
    {
        answers.Enqueue(answer);

        return ValueTask.FromResult(Result<ItemId, AgentError>.Success(answer.Item));
    }

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken)
    {
        results.Enqueue(result);

        return ValueTask.FromResult(Result<ItemId, AgentError>.Success(result.Item));
    }

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref interruptions);

        return ValueTask.FromResult(turns.IsEmpty
            ? Result<TurnId, AgentError>.Failure(AgentError.NoTurnInProgress)
            : Result<TurnId, AgentError>.Success(turns.Last()));
    }

    public void End() => events.Writer.TryComplete();

    public void Crash() => events.Writer.TryComplete(new InvalidOperationException("The scripted agent crashed."));

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        events.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }
}
