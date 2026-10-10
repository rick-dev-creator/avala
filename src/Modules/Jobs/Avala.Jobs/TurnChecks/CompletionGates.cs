using Avala.Jobs.Contracts;

namespace Avala.Jobs.TurnChecks;

internal sealed class CompletionGates(IEnumerable<ICompletionGate> gates)
{
    public async Task<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        foreach (var gate in gates)
        {
            var verdict = await gate.EvaluateAsync(attempt, cancellationToken);

            if (verdict.Decision != GateDecision.Pass)
            {
                return verdict;
            }
        }

        return GateVerdict.Pass;
    }
}
