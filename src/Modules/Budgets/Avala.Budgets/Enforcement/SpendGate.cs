using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class SpendGate(BudgetBook book, JobBreaches breaches, IUsageTally tally, DailyCeiling ceiling) : ICompletionGate
{
    public async ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        (await OverspentAsync(attempt, cancellationToken)).Match(found => GateVerdict.HoldFor(found.Reason), () => GateVerdict.Pass);

    private async Task<Option<BudgetBreach>> OverspentAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        await attempt.Turn.Match(
            async turn =>
            {
                await tally.TalliedAsync(turn.Session, turn.Turn, cancellationToken);

                return await breaches.Overspent(attempt.Job, turn.Session).Match(
                    found => Task.FromResult(Option<BudgetBreach>.Some(found)),
                    () => CeiledAsync(attempt.Job, turn.Session, cancellationToken));
            },
            () => Task.FromResult(book.Overspent(attempt.Job)));

    private async Task<Option<BudgetBreach>> CeiledAsync(JobId job, SessionId session, CancellationToken cancellationToken)
    {
        var running = book.Budgeted(session).Match<(JobId, ConnectionName)[]>(budgeted => [(job, budgeted.Connection)], () => []);

        if (await ceiling.BreachesAsync(running, cancellationToken) is not [var (_, breach), ..])
        {
            return Option<BudgetBreach>.None;
        }

        book.KeepCeiled(job, breach);

        return breach;
    }
}
