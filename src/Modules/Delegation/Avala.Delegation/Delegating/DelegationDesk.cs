using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Reporting;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Delegating;

internal sealed class DelegationDesk(Delegator delegator, ChildReporter reporter, IJobCatalog catalog)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<JobProgressed>, IHandle<JobHeld>
{
    private readonly Dictionary<SessionId, string> worktrees = [];
    private readonly Dictionary<SessionId, JobId> jobs = [];
    private readonly Dictionary<JobId, DelegationRecord> pending = [];
    private readonly HashSet<JobId> awaitingHold = [];
    private readonly Dictionary<JobId, (ItemId Item, string Text)> replies = [];

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        worktrees[integrationEvent.Session] = integrationEvent.WorkingDirectory;

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        jobs[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case ToolCalled { Tool: DelegationTool.Name } called:
                await DelegateAsync(called, cancellationToken);
                break;
            case ItemStarted { Kind: ItemKind.Message } started when IsChild(started.Session, out var writer):
                replies[writer] = (started.Item, string.Empty);
                break;
            case ItemProgressed progressed when IsChild(progressed.Session, out var writer)
                && replies.TryGetValue(writer, out var reply) && reply.Item == progressed.Item:
                replies[writer] = (reply.Item, reply.Text + progressed.Text);
                break;
        }
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            foreach (var orphan in pending.Where(waiting => waiting.Value.Parent == Option<JobId>.Some(integrationEvent.Job)).Select(waiting => waiting.Key).ToList())
            {
                reporter.Discard(orphan, cancellationToken);
            }
        }

        if (!pending.ContainsKey(integrationEvent.Job))
        {
            return;
        }

        if (integrationEvent.Status == JobStatus.NeedsHelp && await WasHeldAsync(integrationEvent.Job, cancellationToken))
        {
            awaitingHold.Add(integrationEvent.Job);
        }
        else if (integrationEvent.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            Settle(integrationEvent.Job, new Settlement(integrationEvent.Status, Option<HoldReason>.None), cancellationToken);
        }
    }

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken)
    {
        if (awaitingHold.Remove(integrationEvent.Hold.Job))
        {
            Settle(integrationEvent.Hold.Job, new Settlement(JobStatus.NeedsHelp, integrationEvent.Hold.Reason), cancellationToken);
        }

        return ValueTask.CompletedTask;
    }

    private async Task DelegateAsync(ToolCalled called, CancellationToken cancellationToken)
    {
        var parent = jobs.TryGetValue(called.Session, out var job) ? job : Option<JobId>.None;
        var caller = new Caller(
            called.Session,
            parent,
            worktrees.TryGetValue(called.Session, out var worktree) ? worktree : Option<string>.None,
            pending.Values.Count(waiting => parent.IsSome && waiting.Parent == parent));
        var delegated = await delegator.DelegateAsync(called, caller, cancellationToken);

        foreach (var (child, record) in delegated.Match<Delegated[]>(started => [started], () => []))
        {
            pending[child] = record;
        }
    }

    private void Settle(JobId child, Settlement settlement, CancellationToken cancellationToken)
    {
        if (!pending.Remove(child, out var delegation))
        {
            return;
        }

        awaitingHold.Remove(child);
        var summary = replies.Remove(child, out var reply) && reply.Text.Length > 0 ? reply.Text : Option<string>.None;
        reporter.Report(delegation, child, settlement, summary, cancellationToken);
    }

    private bool IsChild(SessionId session, out JobId child) =>
        jobs.TryGetValue(session, out child) && pending.ContainsKey(child);

    private async Task<bool> WasHeldAsync(JobId child, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(child, cancellationToken)).Match(
            history => history.Attempts is [.., { Outcome: AttemptOutcome.Interrupted }],
            () => false);
}
