using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Admission;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetBook(IMachineBudgetFile machine, IInterventionStore store) : IBudgets, IStartupTask
{
    private ImmutableDictionary<SessionId, (SessionBudget Budget, ConnectionName Connection)> sessions =
        ImmutableDictionary<SessionId, (SessionBudget Budget, ConnectionName Connection)>.Empty;

    private ImmutableList<BudgetIntervention> earlier = [];
    private ImmutableList<BudgetIntervention> interventions = [];

    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => machine.LoadAsync(cancellationToken);

    public void Keep(SessionBudget budget, ConnectionName connection) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, budget.Session, (budget, connection), (_, _) => (budget, connection));

    public Option<(SessionBudget Budget, ConnectionName Connection)> Budgeted(SessionId session) =>
        Volatile.Read(ref sessions).TryGetValue(session, out var budgeted) ? budgeted : Option<(SessionBudget, ConnectionName)>.None;

    public async Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));
        await store.RecordAsync(intervention, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref earlier, [.. await store.EarlierRunsAsync(cancellationToken)]);

    public Option<SessionBudget> BudgetOf(SessionId session) => Budgeted(session).Map(budgeted => budgeted.Budget);

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) =>
        [.. Volatile.Read(ref earlier).Concat(Volatile.Read(ref interventions)).Where(intervention => intervention.Hold.Job == job)];
}
