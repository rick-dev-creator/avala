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

    private ImmutableDictionary<JobId, BudgetBreach> overspent = ImmutableDictionary<JobId, BudgetBreach>.Empty;

    private ImmutableHashSet<JobId> ended = [];

    private ImmutableList<BudgetIntervention> earlier = [];
    private ImmutableList<BudgetIntervention> interventions = [];
    private ImmutableList<BudgetCarve> earlierCarves = [];
    private ImmutableList<BudgetCarve> carves = [];

    public IReadOnlyList<BudgetCarve> Carves => [.. Volatile.Read(ref earlierCarves), .. Volatile.Read(ref carves)];

    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => machine.LoadAsync(cancellationToken);

    public async Task KeepAsync(SessionBudget budget, ConnectionName connection, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.AddOrUpdate(ref sessions, budget.Session, (budget, connection), (_, _) => (budget, connection));
        await store.RecordAsync(new BudgetedSession(budget, connection), cancellationToken);
    }

    public Option<(SessionBudget Budget, ConnectionName Connection)> Budgeted(SessionId session) =>
        Volatile.Read(ref sessions).TryGetValue(session, out var budgeted) ? budgeted : Option<(SessionBudget, ConnectionName)>.None;

    public async Task RecordAsync(BudgetIntervention intervention, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));
        await store.RecordAsync(intervention, cancellationToken);
    }

    public async Task RecordAsync(BudgetCarve carve, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref carves, recorded => recorded.Add(carve));
        await store.RecordAsync(carve, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref earlier, [.. await store.EarlierRunsAsync(cancellationToken)]);
        Volatile.Write(ref earlierCarves, [.. await store.EarlierCarvesAsync(cancellationToken)]);

        foreach (var budgeted in await store.EarlierBudgetsAsync(cancellationToken))
        {
            ImmutableInterlocked.TryAdd(ref sessions, budgeted.Budget.Session, (budgeted.Budget, budgeted.Connection));
        }
    }

    public Option<SessionBudget> BudgetOf(SessionId session) => Budgeted(session).Map(budgeted => budgeted.Budget);

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) =>
        [.. Volatile.Read(ref earlier).Concat(Volatile.Read(ref interventions)).Where(intervention => intervention.Hold.Job == job)];

    public Option<BudgetBreach> Overspent(JobId job) =>
        Volatile.Read(ref overspent).TryGetValue(job, out var breach) ? breach : Option<BudgetBreach>.None;

    public void KeepSpending(JobId job, Option<BudgetBreach> breach) =>
        ImmutableInterlocked.Update(ref overspent, kept => breach.Match(found => kept.SetItem(job, found), () => kept.Remove(job)));

    public bool Ended(JobId job) => Volatile.Read(ref ended).Contains(job);

    public void KeepStatus(JobId job, JobStatus status) =>
        ImmutableInterlocked.Update(ref ended, known => status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed ? known.Add(job) : known.Remove(job));

    public Option<BudgetCarve> CarveOf(JobId child) => Carves.LastOrDefault(carve => carve.Child == child).ToOption();
}
