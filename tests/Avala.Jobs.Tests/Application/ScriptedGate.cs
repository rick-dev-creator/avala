using System.Collections.Concurrent;
using Avala.Jobs.Contracts;

namespace Avala.Jobs.Tests.Application;

internal sealed class ScriptedGate(params GateVerdict[] verdicts) : ICompletionGate
{
    private readonly ConcurrentQueue<GateVerdict> remaining = new(verdicts);
    private readonly ConcurrentQueue<CompletedAttempt> evaluated = new();

    public IReadOnlyList<CompletedAttempt> Evaluated => [.. evaluated];

    public ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        evaluated.Enqueue(attempt);

        return ValueTask.FromResult(remaining.TryDequeue(out var verdict) ? verdict : GateVerdict.Pass);
    }
}
