using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed class ClaudeCodeSession : IAgentSession
{
    private readonly Channel<IAgentEvent> events = Channel.CreateUnbounded<IAgentEvent>();
    private readonly SerialExecutor stage = new();
    private readonly ICliProcess cli;
    private readonly Conversation conversation;
    private Task pump = Task.CompletedTask;
    private TaskCompletionSource turnEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closed;

    private ClaudeCodeSession(ICliProcess cli, Conversation conversation, SessionId id, Option<AgentAccount> account)
    {
        this.cli = cli;
        this.conversation = conversation;
        Id = id;
        Account = account;
    }

    public SessionId Id { get; }

    public Option<AgentAccount> Account { get; }

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public static async Task<ClaudeCodeSession> OpenAsync(ICliProcess cli, Func<SessionId, Conversation> conversation, Option<AgentAccount> account)
    {
        var id = SessionId.New();
        var session = new ClaudeCodeSession(cli, conversation(id), id, account);
        await session.stage.RunAsync(_ => session.ApplyAsync(Reaction.Send(Messages.Initialize())), CancellationToken.None);
        session.pump = Task.Run(session.PumpAsync, CancellationToken.None);

        return session;
    }

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) =>
        await stage.RunAsync(
            async _ => closed
                ? AgentError.SessionClosed
                : await conversation.Begin(turn).Match(
                    async begun =>
                    {
                        turnEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        await ApplyAsync(begun.Reaction);

                        return Result<TurnId, AgentError>.Success(begun.Turn);
                    },
                    error => Task.FromResult(Result<TurnId, AgentError>.Failure(error))),
            cancellationToken);

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        AnswerWithAsync(() => conversation.Respond(decision), decision.Item, cancellationToken);

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) =>
        AnswerWithAsync(() => conversation.Answer(answer), answer.Item, cancellationToken);

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken) =>
        AnswerWithAsync(() => conversation.Return(result), result.Item, cancellationToken);

    public async ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken)
    {
        var interrupted = await stage.RunAsync(
            async _ => await conversation.Interrupt().Match(
                async begun =>
                {
                    await ApplyAsync(begun.Reaction);

                    return Result<(TurnId, Task), AgentError>.Success((begun.Turn, turnEnded.Task));
                },
                error => Task.FromResult(Result<(TurnId, Task), AgentError>.Failure(error))),
            cancellationToken);

        if (!interrupted.TryGetValue(out var running, out var refused))
        {
            return refused;
        }

        await running.Item2.WaitAsync(cancellationToken);

        return running.Item1;
    }

    public async ValueTask DisposeAsync()
    {
        await stage.RunAsync(
            _ =>
            {
                closed = true;

                return Task.CompletedTask;
            },
            CancellationToken.None);
        await cli.DisposeAsync();
        await pump;
        events.Writer.TryComplete();
        turnEnded.TrySetResult();
        await stage.DisposeAsync();
    }

    private async ValueTask<Result<ItemId, AgentError>> AnswerWithAsync(Func<Result<Reaction, AgentError>> answer, ItemId item, CancellationToken cancellationToken) =>
        await stage.RunAsync(
            async _ => closed
                ? AgentError.SessionClosed
                : await answer().Match(
                    async reaction =>
                    {
                        await ApplyAsync(reaction);

                        return Result<ItemId, AgentError>.Success(item);
                    },
                    error => Task.FromResult(Result<ItemId, AgentError>.Failure(error))),
            cancellationToken);

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var line in cli.Lines)
            {
                foreach (var message in Parsed(line))
                {
                    await stage.RunAsync(_ => closed ? Task.CompletedTask : ApplyAsync(conversation.Receive(message)), CancellationToken.None);
                }
            }
        }
        catch (IOException)
        {
        }

        await stage.RunAsync(
            _ =>
            {
                if (!closed)
                {
                    closed = true;
                    events.Writer.TryComplete(new IOException("Claude Code ended the session."));
                    turnEnded.TrySetResult();
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);
    }

    private async Task ApplyAsync(Reaction reaction)
    {
        foreach (var outgoing in reaction.Outgoing)
        {
            try
            {
                await cli.WriteAsync(outgoing.ToJsonString(), CancellationToken.None);
            }
            catch (IOException)
            {
                break;
            }
        }

        foreach (var agentEvent in reaction.Events)
        {
            events.Writer.TryWrite(agentEvent);

            if (agentEvent is TurnCompleted)
            {
                turnEnded.TrySetResult();
            }
        }
    }

    private static IEnumerable<JsonNode> Parsed(string line)
    {
        JsonNode? message;

        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            message = null;
        }

        return message is null ? [] : [message];
    }
}
