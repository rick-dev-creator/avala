using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Board;

internal sealed class BoardKeeper(IJobCatalog catalog, JobBoard board, TimeProvider time) :
    IHandle<StartupCompleted>,
    IHandle<JobSubmitted>,
    IHandle<JobProgressed>,
    IHandle<JobHeld>,
    IHandle<JobApproved>,
    IHandle<JobSessionStarted>,
    IHandle<AgentActivity>,
    IHandle<CanvasUpdated>,
    IHandle<PermissionDecided>,
    IHandle<FormDecided>,
    IHandle<AttemptVerified>,
    IHandle<PermissionAnswered>,
    IHandle<AutonomyApplied>,
    IHandle<UsageRecorded>,
    IHandle<BudgetIntervened>,
    IHandle<BudgetCarved>,
    IHandle<ChildDelegated>,
    IHandle<ChildReported>
{
    private readonly Dictionary<SessionId, JobId> sessions = [];
    private ImmutableDictionary<JobId, BoardJob> jobs = ImmutableDictionary<JobId, BoardJob>.Empty;

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var summary in (await catalog.ListAsync(cancellationToken)).Where(summary => !jobs.ContainsKey(summary.Job)))
        {
            await RefreshAsync(summary.Job, restored: true, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobSubmitted integrationEvent, CancellationToken cancellationToken) =>
        await RefreshAsync(integrationEvent.Job, restored: false, cancellationToken);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        await RefreshAsync(integrationEvent.Job, restored: true, cancellationToken);
        Change(integrationEvent.Job, job => Audited(job with
        {
            Summary = job.Summary with { Status = integrationEvent.Status },
            Hold = integrationEvent.Status == JobStatus.NeedsHelp ? job.Hold : Option<HoldReason>.None,
        }));
    }

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken) =>
        ChangedAsync(integrationEvent.Hold.Job, job => Audited(job with { Hold = integrationEvent.Hold.Reason }));

    public ValueTask HandleAsync(JobApproved integrationEvent, CancellationToken cancellationToken) =>
        ChangedAsync(integrationEvent.Approval.Job, job => Audited(job with { Delivery = integrationEvent.Approval.Delivery }));

    public ValueTask HandleAsync(AttemptVerified integrationEvent, CancellationToken cancellationToken) =>
        ChangedAsync(integrationEvent.Report.Job, job => Audited(job with { Verification = integrationEvent.Report }));

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        if (!jobs.ContainsKey(integrationEvent.Job))
        {
            await RefreshAsync(integrationEvent.Job, restored: true, cancellationToken);
        }

        Change(integrationEvent.Job, Audited);
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Event.Session, job => job with { Transcript = job.Transcript.Apply(integrationEvent.Event, time.GetUtcNow()) });

    public ValueTask HandleAsync(CanvasUpdated integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Snapshot.Session, job => job with { Transcript = job.Transcript.Apply(integrationEvent.Snapshot) });

    public ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Decision.Session, job => Audited(job with { Transcript = job.Transcript.Apply(integrationEvent.Decision) }));

    public ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Decision.Session, job => Audited(job with { Transcript = job.Transcript.Apply(integrationEvent.Decision) }));

    public ValueTask HandleAsync(PermissionAnswered integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Answer.Session, Audited);

    public ValueTask HandleAsync(AutonomyApplied integrationEvent, CancellationToken cancellationToken) =>
        AuditedAsync(integrationEvent.Autonomy.Job);

    public ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken) =>
        AuditedAsync([.. integrationEvent.Job.Match<JobId[]>(job => [job], () => [])]);

    public ValueTask HandleAsync(BudgetIntervened integrationEvent, CancellationToken cancellationToken) =>
        AuditedAsync(integrationEvent.Intervention.Hold.Job);

    public ValueTask HandleAsync(BudgetCarved integrationEvent, CancellationToken cancellationToken) =>
        AuditedAsync(integrationEvent.Carve.Parent, integrationEvent.Carve.Child);

    public ValueTask HandleAsync(ChildDelegated integrationEvent, CancellationToken cancellationToken) =>
        AuditedAsync(Related(integrationEvent.Delegation));

    public ValueTask HandleAsync(ChildReported integrationEvent, CancellationToken cancellationToken) =>
        AuditedAsync(Related(integrationEvent.Delegation));

    private static BoardJob Audited(BoardJob job) => job with { Revision = job.Revision + 1 };

    private static JobId[] Related(DelegationRecord delegation) =>
        [.. delegation.Parent.Match<JobId[]>(parent => [parent], () => []), .. delegation.Child.Match<JobId[]>(child => [child], () => [])];

    private async Task RefreshAsync(JobId job, bool restored, CancellationToken cancellationToken) =>
        _ = (await catalog.HistoryAsync(job, cancellationToken)).Match(
            found => Change(
                job,
                known => known with
                {
                    Attempts = found.Attempts.Count,
                    Transcript = known.Transcript.WithPrompts(found.Summary.Instruction, found.Attempts),
                },
                () => Joined(found, restored)),
            () => false);

    private static BoardJob Joined(JobHistory history, bool restored)
    {
        var transcript = Transcript.Empty.WithPrompts(history.Summary.Instruction, history.Attempts);

        return new BoardJob(history.Summary, restored && history.Attempts.Count > 0 ? transcript.WithRestart() : transcript)
        {
            Attempts = history.Attempts.Count,
        };
    }

    private ValueTask AuditedAsync(params JobId[] audited)
    {
        foreach (var job in audited)
        {
            Change(job, Audited);
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask InSessionAsync(SessionId session, Func<BoardJob, BoardJob> change) =>
        sessions.TryGetValue(session, out var job) ? ChangedAsync(job, change) : ValueTask.CompletedTask;

    private ValueTask ChangedAsync(JobId job, Func<BoardJob, BoardJob> change)
    {
        Change(job, change);

        return ValueTask.CompletedTask;
    }

    private void Change(JobId job, Func<BoardJob, BoardJob> change) =>
        _ = Change(job, change, () => Option<BoardJob>.None);

    private bool Change(JobId job, Func<BoardJob, BoardJob> change, Func<Option<BoardJob>> joined) =>
        (jobs.TryGetValue(job, out var known) ? Option<BoardJob>.Some(change(known)) : joined()).Match(
            updated =>
            {
                jobs = jobs.SetItem(job, updated);
                board.Publish(jobs);

                return true;
            },
            () => false);
}
