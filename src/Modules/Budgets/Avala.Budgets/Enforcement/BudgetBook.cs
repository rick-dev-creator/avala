using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetBook : IBudgets
{
    private ImmutableDictionary<SessionId, (SessionBudget Budget, ProviderInfo Provider)> sessions =
        ImmutableDictionary<SessionId, (SessionBudget Budget, ProviderInfo Provider)>.Empty;

    private ImmutableList<BudgetIntervention> interventions = [];

    public void Keep(SessionBudget budget, ProviderInfo provider) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, budget.Session, (budget, provider), (_, _) => (budget, provider));

    public Option<(SessionBudget Budget, ProviderInfo Provider)> Budgeted(SessionId session) =>
        Volatile.Read(ref sessions).TryGetValue(session, out var budgeted) ? budgeted : Option<(SessionBudget, ProviderInfo)>.None;

    public void Record(BudgetIntervention intervention) =>
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));

    public Option<SessionBudget> BudgetOf(SessionId session) => Budgeted(session).Map(budgeted => budgeted.Budget);

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) =>
        [.. Volatile.Read(ref interventions).Where(intervention => intervention.Hold.Job == job)];
}
