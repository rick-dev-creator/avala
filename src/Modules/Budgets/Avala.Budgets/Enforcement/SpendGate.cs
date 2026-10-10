using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class SpendGate(BudgetBook book, JobBreaches breaches, IUsageTally tally) : ICompletionGate
{
    public async ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        (await OverspentAsync(attempt, cancellationToken)).Match(_ => GateVerdict.HoldFor(HoldReason.BudgetExceeded), () => GateVerdict.Pass);

    private async Task<Option<BudgetBreach>> OverspentAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        await attempt.Turn.Match(
            async turn =>
            {
                await tally.TalliedAsync(turn.Session, turn.Turn, cancellationToken);

                return breaches.Overspent(attempt.Job, turn.Session);
            },
            () => Task.FromResult(book.Overspent(attempt.Job)));
}
