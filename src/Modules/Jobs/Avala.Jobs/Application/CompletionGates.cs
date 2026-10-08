using Avala.Jobs.Contracts;

namespace Avala.Jobs.Application;

internal sealed class CompletionGates(IEnumerable<ICompletionGate> gates)
{
    public async Task<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        foreach (var gate in gates)
        {
            var verdict = await gate.EvaluateAsync(attempt, cancellationToken);

            if (verdict.Decision == GateDecision.Retry)
            {
                return verdict;
            }
        }

        return GateVerdict.Pass;
    }
}
