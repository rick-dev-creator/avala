using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Transcripts.Contracts;

namespace Avala.Transcripts.Keeping;

internal sealed class TranscriptKeeper(IJobCatalog catalog, ITranscriptLog log, TimeProvider time) :
    IHandle<JobSessionStarted>,
    IHandle<JobProgressed>,
    IHandle<AgentActivity>,
    IHandle<CanvasUpdated>,
    IHandle<PermissionDecided>,
    IHandle<FormDecided>
{
    private readonly Dictionary<SessionId, JobId> sessions = [];
    private readonly Dictionary<JobId, int> marked = [];
    private readonly TranscriptBounds bounds = new();

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var job = integrationEvent.Job;
        var latest = (await catalog.HistoryAsync(job, cancellationToken)).Match(history => history.Attempts.Select(attempt => attempt.Number).DefaultIfEmpty().Max(), () => 0);
        var known = marked.TryGetValue(job, out var count) ? count : await log.AttemptsAsync(job, cancellationToken);

        for (var attempt = known + 1; attempt <= latest; attempt++)
        {
            log.Keep(job, time.GetUtcNow(), new AttemptBegan(attempt));
        }

        marked[job] = Math.Max(known, latest);
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(integrationEvent.Event.Session, bounds.Bounded(integrationEvent.Event).Map<ITranscriptFact>(activity => new AgentActed(activity)));

    public ValueTask HandleAsync(CanvasUpdated integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(integrationEvent.Snapshot.Session, new CanvasDrawn(TranscriptBounds.Bounded(integrationEvent.Snapshot)));

    public ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(integrationEvent.Decision.Session, new PermissionRuled(integrationEvent.Decision));

    public ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken) =>
        KeepAsync(integrationEvent.Decision.Session, new FormRuled(integrationEvent.Decision));

    private ValueTask KeepAsync(SessionId session, Option<ITranscriptFact> fact)
    {
        if (sessions.TryGetValue(session, out var job))
        {
            _ = fact.Map(kept =>
            {
                log.Keep(job, time.GetUtcNow(), kept);

                return true;
            });
        }

        return ValueTask.CompletedTask;
    }
}
