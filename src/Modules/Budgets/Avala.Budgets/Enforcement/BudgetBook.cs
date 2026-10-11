using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Admission;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetBook(IMachineBudgetFile machine, IInterventionStore store) : IStartupTask
{
    private ImmutableDictionary<SessionId, (SessionBudget Budget, ConnectionName Connection)> sessions =
        ImmutableDictionary<SessionId, (SessionBudget Budget, ConnectionName Connection)>.Empty;

    private ImmutableDictionary<JobId, BudgetBreach> overspent = ImmutableDictionary<JobId, BudgetBreach>.Empty;

    private ImmutableDictionary<JobId, BudgetBreach> ceiled = ImmutableDictionary<JobId, BudgetBreach>.Empty;

    private ImmutableHashSet<JobId> ended = [];

    private ImmutableDictionary<JobId, SessionId> ties = ImmutableDictionary<JobId, SessionId>.Empty;

    private ImmutableList<BudgetIntervention> earlier = [];
    private ImmutableList<BudgetIntervention> interventions = [];
    private ImmutableList<BudgetCarve> earlierCarves = [];
    private ImmutableList<BudgetCarve> carves = [];
    private ImmutableList<BudgetRaise> earlierRaises = [];
    private ImmutableList<BudgetRaise> raises = [];

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

    public async Task RecordAsync(BudgetRaise raise, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref raises, recorded => recorded.Add(raise));
        await store.RecordAsync(raise, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref earlierRaises, [.. await store.EarlierRaisesAsync(cancellationToken)]);
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

    public void KeepCeiled(JobId job, BudgetBreach breach) => ImmutableInterlocked.AddOrUpdate(ref ceiled, job, breach, (_, _) => breach);

    public Option<BudgetBreach> TakeCeiled(JobId job) =>
        ImmutableInterlocked.TryRemove(ref ceiled, job, out var breach) ? breach : Option<BudgetBreach>.None;

    public void Tie(JobId job, SessionId session) => ImmutableInterlocked.AddOrUpdate(ref ties, job, session, (_, _) => session);

    public Option<SessionId> SessionOf(JobId job) =>
        Volatile.Read(ref ties).TryGetValue(job, out var session) ? session
        : OfJob(job) is [.., var latest] ? latest.Hold.Session
        : Option<SessionId>.None;

    public bool Ended(JobId job) => Volatile.Read(ref ended).Contains(job);

    public void KeepStatus(JobId job, JobStatus status) =>
        ImmutableInterlocked.Update(ref ended, known => status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed ? known.Add(job) : known.Remove(job));

    public Option<BudgetCarve> CarveOf(JobId child) => Carves.LastOrDefault(carve => carve.Child == child).ToOption();

    public Option<BudgetCarve> FirstCarveOf(JobId child) => Carves.FirstOrDefault(carve => carve.Child == child).ToOption();

    public IReadOnlyList<BudgetRaise> RaisesOf(JobId job) =>
        [.. Volatile.Read(ref earlierRaises).Concat(Volatile.Read(ref raises)).Where(raise => raise.Job == job)];

    public BudgetCaps Allowance(JobId job, BudgetCaps caps) => caps.Plus(RaisesOf(job)).Within(CarveOf(job));
}
