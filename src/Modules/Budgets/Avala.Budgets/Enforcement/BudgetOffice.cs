using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetOffice(BudgetBook book, JobBreaches breaches, BudgetActions actions) : IBudgets, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();

    public Option<SessionBudget> BudgetOf(SessionId session) => book.BudgetOf(session);

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) => book.OfJob(job);

    public Option<BudgetCarve> CarveOf(JobId child) => book.CarveOf(child);

    public IReadOnlyList<BudgetRaise> RaisesOf(JobId job) => book.RaisesOf(job);

    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => book.MachineAsync(cancellationToken);

    public async ValueTask<Result<BudgetRaise, BudgetError>> RaiseAsync(JobId job, CancellationToken cancellationToken) =>
        await owner.RunAsync(
            async token => await RaisableBy(job).Match(
                async by =>
                {
                    var raise = new BudgetRaise(job, by.By, actions.Now);
                    await actions.RaiseAsync(raise, token);
                    await RaiseLineAsync(job, by.By, by.Committed, token);

                    return Result<BudgetRaise, BudgetError>.Success(raise);
                },
                () => Task.FromResult(Result<BudgetRaise, BudgetError>.Failure(BudgetError.NotOverBudget))),
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private Option<(SpendCaps By, Commitment Committed)> RaisableBy(JobId job) =>
        book.OfJob(job) is [.., { Hold.Reason: HoldReason.BudgetExceeded, Breach.Measure: BudgetMeasure.Cost or BudgetMeasure.Tokens }] && !book.Ended(job)
            ? book.SessionOf(job).Bind(book.Budgeted).Bind(budgeted =>
            {
                var committed = breaches.Tree.CommittedBy(job);

                return book.Allowance(job, budgeted.Budget.Caps)
                    .RaiseFrom(budgeted.Budget.Caps.Within(book.FirstCarveOf(job)), committed)
                    .Map(by => (by, committed));
            })
            : Option<(SpendCaps, Commitment)>.None;

    private async Task RaiseLineAsync(JobId child, SpendCaps by, Commitment committed, CancellationToken cancellationToken)
    {
        var growth = by;

        foreach (var carve in LineOf(child))
        {
            var raised = carve.Plus(growth, actions.Now);
            growth = carve.Child == child ? carve.GrowthTo(raised, committed) : growth;
            await actions.RecarveAsync(raised, cancellationToken);
            await actions.RaiseAsync(new BudgetRaise(carve.Parent, growth, actions.Now) { Child = child }, cancellationToken);
        }
    }

    private IReadOnlyList<BudgetCarve> LineOf(JobId job) =>
        book.CarveOf(job).Match<IReadOnlyList<BudgetCarve>>(carve => [carve, .. LineOf(carve.Parent)], () => []);
}
