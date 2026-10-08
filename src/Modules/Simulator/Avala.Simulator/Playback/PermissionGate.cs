using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Playback;

internal sealed class PermissionGate
{
    private readonly Lock gate = new();
    private Pending? pending;

    public Task<PermissionAnswer> ExpectAsync(ItemId item)
    {
        var answer = new TaskCompletionSource<PermissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (gate)
        {
            pending = new Pending(item, answer);
        }

        return answer.Task;
    }

    public Result<ItemId, AgentError> Respond(PermissionDecision decision)
    {
        lock (gate)
        {
            if (pending is not { } waiting || waiting.Item != decision.Item)
            {
                return AgentError.NoPendingPermission;
            }

            pending = null;
            waiting.Answer.TrySetResult(decision.Answer);

            return decision.Item;
        }
    }

    public void Withdraw(ItemId item)
    {
        lock (gate)
        {
            if (pending?.Item == item)
            {
                pending = null;
            }
        }
    }

    private sealed record Pending(ItemId Item, TaskCompletionSource<PermissionAnswer> Answer);
}
