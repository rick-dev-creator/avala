using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Admission;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetBook(IMachineBudgetFile machine) : IBudgets
{
    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => machine.LoadAsync(cancellationToken);

    private ImmutableDictionary<SessionId, (SessionBudget Budget, ConnectionName Connection)> sessions =
        ImmutableDictionary<SessionId, (SessionBudget Budget, ConnectionName Connection)>.Empty;

    private ImmutableList<BudgetIntervention> interventions = [];

    public void Keep(SessionBudget budget, ConnectionName connection) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, budget.Session, (budget, connection), (_, _) => (budget, connection));

    public Option<(SessionBudget Budget, ConnectionName Connection)> Budgeted(SessionId session) =>
        Volatile.Read(ref sessions).TryGetValue(session, out var budgeted) ? budgeted : Option<(SessionBudget, ConnectionName)>.None;

    public void Record(BudgetIntervention intervention) =>
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));

    public Option<SessionBudget> BudgetOf(SessionId session) => Budgeted(session).Map(budgeted => budgeted.Budget);

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) =>
        [.. Volatile.Read(ref interventions).Where(intervention => intervention.Hold.Job == job)];
}
