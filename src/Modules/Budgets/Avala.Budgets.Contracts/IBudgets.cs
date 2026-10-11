using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Contracts;

public interface IBudgets
{
    Option<SessionBudget> BudgetOf(SessionId session);

    IReadOnlyList<BudgetIntervention> OfJob(JobId job);

    Option<BudgetCarve> CarveOf(JobId child);

    ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken);

    ValueTask<Result<BudgetRaise, BudgetError>> RaiseAsync(JobId job, CancellationToken cancellationToken);

    IReadOnlyList<BudgetRaise> RaisesOf(JobId job);
}

public interface IRepositoryBudgets
{
    ValueTask<RepositoryBudget> OfRepositoryAsync(string repository, CancellationToken cancellationToken);
}
