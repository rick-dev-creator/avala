using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Spending;

internal sealed record RaiseOutcome(Result<BudgetRaise, BudgetError> Raised, Option<JobRejection> NotContinued);

internal sealed class RaiseDesk(IBudgets budgets, IJobs jobs)
{
    public async Task<RaiseOutcome> RaiseAndContinueAsync(JobId job, Func<BudgetRaise, string> message, CancellationToken cancellationToken)
    {
        var raised = await budgets.RaiseAsync(job, cancellationToken);

        return await raised.Match(
            async raise => new RaiseOutcome(
                raised,
                (await jobs.ContinueAsync(job, message(raise), cancellationToken)).Match(_ => Option<JobRejection>.None, rejection => rejection)),
            _ => Task.FromResult(new RaiseOutcome(raised, Option<JobRejection>.None)));
    }
}
