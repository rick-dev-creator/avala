using Avala.Jobs.Contracts;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class BlockingGate : ICompletionGate
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int evaluations;

    public Task Entered => entered.Task;

    public void Release() => release.TrySetResult();

    public async ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref evaluations) == 1)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        return GateVerdict.Pass;
    }
}
