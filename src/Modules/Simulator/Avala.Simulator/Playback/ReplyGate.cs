using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Playback;

internal sealed class ReplyGate<TReply>(SerialExecutor stage, AgentError nothingPending)
{
    private Option<Pending> pending;

    public Task<Task<TReply>> ExpectAsync(ItemId item, CancellationToken cancellationToken) =>
        stage.RunAsync(
            _ =>
            {
                var reply = new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = new Pending(item, reply);

                return Task.FromResult(reply.Task);
            },
            cancellationToken);

    public Task<Result<ItemId, AgentError>> RespondAsync(ItemId item, TReply reply, CancellationToken cancellationToken) =>
        stage.RunAsync(_ => Task.FromResult(Respond(item, reply)), cancellationToken);

    public async Task<TReply> AwaitAsync(ItemId item, Task<TReply> reply, CancellationToken cancellationToken)
    {
        try
        {
            return await reply.WaitAsync(cancellationToken);
        }
        finally
        {
            await WithdrawAsync(item);
        }
    }

    public Task WithdrawAsync(ItemId item) =>
        stage.RunAsync(
            _ =>
            {
                pending = pending.Bind(waiting => waiting.Item == item ? Option<Pending>.None : waiting);

                return Task.CompletedTask;
            },
            CancellationToken.None);

    private Result<ItemId, AgentError> Respond(ItemId item, TReply reply) =>
        pending.Bind(waiting => waiting.Item == item ? waiting : Option<Pending>.None).Match(
            waiting =>
            {
                pending = Option<Pending>.None;
                waiting.Reply.TrySetResult(reply);

                return Result<ItemId, AgentError>.Success(item);
            },
            () => nothingPending);

    private sealed record Pending(ItemId Item, TaskCompletionSource<TReply> Reply);
}
