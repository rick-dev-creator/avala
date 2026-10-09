using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Playback;

internal sealed class PermissionGate(SerialExecutor stage)
{
    private Option<Pending> pending;

    public Task<Task<PermissionAnswer>> ExpectAsync(ItemId item, CancellationToken cancellationToken) =>
        stage.RunAsync(
            _ =>
            {
                var answer = new TaskCompletionSource<PermissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = new Pending(item, answer);

                return Task.FromResult(answer.Task);
            },
            cancellationToken);

    public Task<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        stage.RunAsync(_ => Task.FromResult(Respond(decision)), cancellationToken);

    public Task WithdrawAsync(ItemId item) =>
        stage.RunAsync(
            _ =>
            {
                pending = pending.Bind(waiting => waiting.Item == item ? Option<Pending>.None : waiting);

                return Task.CompletedTask;
            },
            CancellationToken.None);

    private Result<ItemId, AgentError> Respond(PermissionDecision decision) =>
        pending.Bind(waiting => waiting.Item == decision.Item ? waiting : Option<Pending>.None).Match(
            waiting =>
            {
                pending = Option<Pending>.None;
                waiting.Answer.TrySetResult(decision.Answer);

                return Result<ItemId, AgentError>.Success(decision.Item);
            },
            () => AgentError.NoPendingPermission);

    private sealed record Pending(ItemId Item, TaskCompletionSource<PermissionAnswer> Answer);
}
