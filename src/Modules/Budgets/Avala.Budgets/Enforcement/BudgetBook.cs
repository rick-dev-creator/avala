using System.Collections.Concurrent;
using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Enforcement;

internal sealed class BudgetBook : IBudgets
{
    private readonly ConcurrentDictionary<SessionId, (SessionBudget Budget, ProviderInfo Provider)> sessions = new();
    private readonly ConcurrentDictionary<JobId, SessionId> jobs = new();
    private readonly ConcurrentDictionary<JobId, JobStatus> statuses = new();
    private ImmutableList<BudgetIntervention> interventions = [];

    public void Keep(SessionBudget budget, ProviderInfo provider) => sessions[budget.Session] = (budget, provider);

    public void Tie(JobId job, SessionId session) => jobs[job] = session;

    public void Track(JobId job, JobStatus status) => statuses[job] = status;

    public bool IsRunning(JobId job) => statuses.GetValueOrDefault(job) == JobStatus.Running;

    public Option<(SessionBudget Budget, ProviderInfo Provider)> BudgetOfJob(JobId job) =>
        jobs.TryGetValue(job, out var session) && sessions.TryGetValue(session, out var budgeted)
            ? budgeted
            : Option<(SessionBudget, ProviderInfo)>.None;

    public void Record(BudgetIntervention intervention) =>
        ImmutableInterlocked.Update(ref interventions, recorded => recorded.Add(intervention));

    public Option<SessionBudget> BudgetOf(SessionId session) =>
        sessions.TryGetValue(session, out var budgeted) ? budgeted.Budget : Option<SessionBudget>.None;

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) => [.. interventions.Where(intervention => intervention.Hold.Job == job)];
}
