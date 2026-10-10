using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Workbench.Inspection;

internal sealed record AuditFacts(IReadOnlyList<VerificationReport> Verifications, IReadOnlyList<PolicyDecision> Decisions, IReadOnlyList<FormDecision> Forms)
{
    public IReadOnlyList<HumanAnswer> Answers { get; init; } = [];

    public Option<SessionAutonomy> Autonomy { get; init; }

    public Option<UsageSummary> Usage { get; init; }

    public Option<SessionBudget> Budget { get; init; }

    public IReadOnlyList<BudgetIntervention> Interventions { get; init; } = [];

    public Option<BudgetCarve> Carve { get; init; }
}

internal sealed class JobAudit(IVerifications verifications, IPermissionAudit audit, IUsage usage, IBudgets budgets)
{
    public AuditFacts Of(JobHistory history)
    {
        var job = history.Summary.Job;
        var sessions = history.Sessions.Select(session => session.Session).Reverse().ToList();

        return new AuditFacts(verifications.OfJob(job), audit.OfJob(job), audit.FormsOfJob(job))
        {
            Answers = audit.AnswersOfJob(job),
            Autonomy = sessions.Select(audit.AutonomyOf).FirstOrDefault(found => found.IsSome),
            Usage = usage.OfJob(job),
            Budget = sessions.Select(budgets.BudgetOf).FirstOrDefault(found => found.IsSome),
            Interventions = budgets.OfJob(job),
            Carve = budgets.CarveOf(job),
        };
    }
}

internal sealed record InspectorFacts(JobRecord Record, AuditFacts Audit)
{
    public Option<ConnectionChoice> Choice { get; init; }

    public IReadOnlyList<HandoffRecord> Handoffs { get; init; } = [];

    public Option<ResetWait> Wait { get; init; }

    public Option<ModelReported> Ran { get; init; }
}

internal sealed class JobInspection(JobRecords records, JobAudit audit, Board.JobBoard board)
{
    public async Task<Option<InspectorFacts>> ReadAsync(JobId job, CancellationToken cancellationToken) =>
        (await records.ReadAsync(job, cancellationToken)).Map(record => new InspectorFacts(record, audit.Of(record.History))
        {
            Choice = record.History.Choice.IsSome ? record.History.Choice : board.Find(job).Bind(found => found.Choice),
            Handoffs = board.Find(job).Match(found => found.Handoffs, () => []),
            Wait = board.Find(job).Bind(found => found.Wait),
            Ran = board.Find(job).Bind(found => found.Transcript.Ran),
        });
}
