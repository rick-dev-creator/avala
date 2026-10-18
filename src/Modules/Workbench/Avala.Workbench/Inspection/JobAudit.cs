using Avala.Contests.Contracts;
using Avala.Forges.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Inspection;

internal sealed record AuditFacts(IReadOnlyList<VerificationReport> Verifications, IReadOnlyList<PolicyDecision> Decisions, IReadOnlyList<FormDecision> Forms)
{
    public IReadOnlyList<HumanAnswer> Answers { get; init; } = [];

    public IReadOnlyList<HumanAnswer> Given { get; init; } = [];

    public Option<SessionAutonomy> Autonomy { get; init; }

    public Option<UsageSummary> Usage { get; init; }

    public Option<SessionBudget> Budget { get; init; }

    public IReadOnlyList<BudgetIntervention> Interventions { get; init; } = [];

    public Option<BudgetCarve> Carve { get; init; }

    public IReadOnlyList<BudgetRaise> Raises { get; init; } = [];
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
            Given = audit.AnswersGivenBy(job),
            Autonomy = sessions.Select(audit.AutonomyOf).FirstOrDefault(found => found.IsSome),
            Usage = usage.OfJob(job),
            Budget = sessions.Select(budgets.BudgetOf).FirstOrDefault(found => found.IsSome),
            Interventions = budgets.OfJob(job),
            Carve = budgets.CarveOf(job),
            Raises = budgets.RaisesOf(job),
        };
    }
}

internal sealed record InspectorFacts(JobRecord Record, AuditFacts Audit)
{
    public Option<ConnectionChoice> Choice { get; init; }

    public IReadOnlyList<HandoffRecord> Handoffs { get; init; } = [];

    public Option<ResetWait> Wait { get; init; }

    public Option<ModelReported> Ran { get; init; }

    public JobStatus Status { get; init; } = Record.History.Summary.Status;

    public Option<PullRequestWatchState> PullRequest { get; init; }

    public IReadOnlyList<WakeUpRecord> WakeUps { get; init; } = [];

    public Option<ContestRecord> Contest { get; init; }

    public IReadOnlyList<ReportedRun> Reported { get; init; } = [];

    public bool Edited { get; init; }
}

internal sealed class JobInspection(JobRecords records, JobAudit audit, Board.JobBoard board)
{
    public async Task<Option<InspectorFacts>> EssentialsAsync(JobId job, CancellationToken cancellationToken) =>
        (await records.HistoryAsync(job, cancellationToken)).Map(record => new InspectorFacts(record, audit.Of(record.History))
        {
            Choice = record.History.Choice.IsSome ? record.History.Choice : board.Find(job).Bind(found => found.Choice),
            Handoffs = board.Find(job).Match(found => found.Handoffs, () => []),
            Wait = board.Find(job).Bind(found => found.Wait),
            Ran = board.Find(job).Bind(found => found.Transcript.Ran),
            Status = board.Find(job).Match(found => found.Status, () => record.History.Summary.Status),
            PullRequest = board.Find(job).Bind(found => found.PullRequest),
            WakeUps = board.Find(job).Match(found => found.WakeUps, () => []),
            Contest = board.ContestOf(job).Map(found => found.Contest),
            Reported = board.Find(job).Match(found => ReportedRuns.AfterLastEdit(found.Transcript.Entries), () => []),
            Edited = board.Find(job).Match(found => ReportedRuns.Edited(found.Transcript.Entries), () => false),
        });

    public async Task<Option<InspectorFacts>> CompleteAsync(Task<Option<InspectorFacts>> essentials, CancellationToken cancellationToken) =>
        await (await essentials).Match(
            async facts => Option<InspectorFacts>.Some(facts with { Record = await records.CompleteAsync(facts.Record, cancellationToken) }),
            () => Task.FromResult(Option<InspectorFacts>.None));
}
