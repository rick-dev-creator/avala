using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
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
    IHandle<AttemptVerified>
{
    private readonly Dictionary<SessionId, JobId> sessions = [];
    private ImmutableDictionary<JobId, BoardJob> jobs = ImmutableDictionary<JobId, BoardJob>.Empty;

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var summary in await catalog.ListAsync(cancellationToken))
        {
            if (!jobs.ContainsKey(summary.Job))
            {
                await RefreshAsync(summary.Job, restored: true, cancellationToken);
            }
        }
    }

    public async ValueTask HandleAsync(JobSubmitted integrationEvent, CancellationToken cancellationToken) =>
        await RefreshAsync(integrationEvent.Job, restored: false, cancellationToken);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        await RefreshAsync(integrationEvent.Job, restored: true, cancellationToken);
        Change(integrationEvent.Job, job => job with
        {
            Summary = job.Summary with { Status = integrationEvent.Status },
            Hold = integrationEvent.Status == JobStatus.NeedsHelp ? job.Hold : Option<HoldReason>.None,
        });
    }

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken) =>
        ChangedAsync(integrationEvent.Hold.Job, job => job with { Hold = integrationEvent.Hold.Reason });

    public ValueTask HandleAsync(JobApproved integrationEvent, CancellationToken cancellationToken) =>
        ChangedAsync(integrationEvent.Approval.Job, job => job with { Delivery = integrationEvent.Approval.Delivery });

    public ValueTask HandleAsync(AttemptVerified integrationEvent, CancellationToken cancellationToken) =>
        ChangedAsync(integrationEvent.Report.Job, job => job with { Verification = integrationEvent.Report });

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        if (!jobs.ContainsKey(integrationEvent.Job))
        {
            await RefreshAsync(integrationEvent.Job, restored: true, cancellationToken);
        }
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Event.Session, transcript => transcript.Apply(integrationEvent.Event, time.GetUtcNow()));

    public ValueTask HandleAsync(CanvasUpdated integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Snapshot.Session, transcript => transcript.Apply(integrationEvent.Snapshot));

    public ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Decision.Session, transcript => transcript.Apply(integrationEvent.Decision));

    public ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken) =>
        InSessionAsync(integrationEvent.Decision.Session, transcript => transcript.Apply(integrationEvent.Decision));

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

    private ValueTask InSessionAsync(SessionId session, Func<Transcript, Transcript> change) =>
        sessions.TryGetValue(session, out var job)
            ? ChangedAsync(job, known => known with { Transcript = change(known.Transcript) })
            : ValueTask.CompletedTask;

    private ValueTask ChangedAsync(JobId job, Func<BoardJob, BoardJob> change)
    {
        Change(job, change);

        return ValueTask.CompletedTask;
    }

    private bool Change(JobId job, Func<BoardJob, BoardJob> change) =>
        Change(job, change, () => Option<BoardJob>.None);

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
