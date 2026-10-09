using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Contracts;

public interface IBudgets
{
    Option<SessionBudget> BudgetOf(SessionId session);

    IReadOnlyList<BudgetIntervention> OfJob(JobId job);
}
