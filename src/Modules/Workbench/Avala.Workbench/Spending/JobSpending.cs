using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Workbench.Following;

namespace Avala.Workbench.Spending;

internal sealed record JobSpend(Option<SessionSeen> Session, Option<UsageSummary> Usage, Option<BudgetCaps> Caps, Option<BudgetCarve> Carve)
{
    public IReadOnlyList<Cost> Spent => Usage.Match(usage => usage.Costs, () => []);

    public long Tokens => Usage.Match(usage => usage.Tokens.Total(), () => 0L);
}

internal sealed record JobIntervention(JobId Job, DateTimeOffset At, HoldReason Reason)
{
    public Option<BudgetBreach> Breach { get; init; }

    public Option<SilenceMeasure> Silence { get; init; }
}

internal static class TokenTotals
{
    extension(TokenUsage tokens)
    {
        public long Total() => tokens.Input + tokens.Output + tokens.CacheRead + tokens.CacheWrite + tokens.Reasoning;
    }
}

internal sealed class JobSpending(IUsage usage, IBudgets budgets, ISupervision supervision, SessionBook sessions)
{
    public JobSpend Of(JobId job)
    {
        var session = sessions.LatestOf(job);

        return new JobSpend(session, usage.OfJob(job), session.Bind(seen => CapsOf(seen.Session)), budgets.CarveOf(job));
    }

    public Option<BudgetCaps> CapsOn(ConnectionName connection) =>
        sessions.LatestOn(connection).Bind(seen => CapsOf(seen.Session));

    public IReadOnlyList<JobIntervention> InterventionsOf(JobId job) =>
    [
        .. budgets.OfJob(job)
            .Select(intervention => new JobIntervention(job, intervention.At, intervention.Hold.Reason) { Breach = intervention.Breach })
            .Concat(supervision.OfJob(job).Select(intervention => new JobIntervention(job, intervention.At, intervention.Hold.Reason) { Silence = intervention.Silence }))
            .OrderBy(intervention => intervention.At),
    ];

    private Option<BudgetCaps> CapsOf(SessionId session) => budgets.BudgetOf(session).Map(budget => budget.Caps);
}
