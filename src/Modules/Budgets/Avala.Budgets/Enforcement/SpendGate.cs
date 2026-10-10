using Avala.Jobs.Contracts;

namespace Avala.Budgets.Enforcement;

internal sealed class SpendGate(BudgetBook book) : ICompletionGate
{
    public ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(book.Overspent(attempt.Job).Match(_ => GateVerdict.HoldFor(HoldReason.BudgetExceeded), () => GateVerdict.Pass));
}
