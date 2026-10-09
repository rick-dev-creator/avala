using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Playback;

internal sealed class ReplyGate<TReply>(SerialExecutor stage, AgentError nothingPending)
{
    private readonly Dictionary<ItemId, TaskCompletionSource<TReply>> pending = [];

    public Task<Task<TReply>> ExpectAsync(ItemId item, CancellationToken cancellationToken) =>
        stage.RunAsync(
            _ =>
            {
                var reply = new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending[item] = reply;

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
                pending.Remove(item);

                return Task.CompletedTask;
            },
            CancellationToken.None);

    private Result<ItemId, AgentError> Respond(ItemId item, TReply reply)
    {
        if (!pending.Remove(item, out var waiting))
        {
            return nothingPending;
        }

        waiting.TrySetResult(reply);

        return item;
    }
}
