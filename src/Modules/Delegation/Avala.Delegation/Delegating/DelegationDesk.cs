using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Delegating;

internal sealed class DelegationDesk(Delegator delegator, ChildReporter reporter, IJobCatalog catalog, DelegationBook book)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<JobProgressed>, IHandle<JobHeld>, IHandle<StartupCompleted>
{
    private static readonly JobStatus[] Settled = [JobStatus.AwaitingReview, JobStatus.NeedsHelp, JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    private readonly Dictionary<SessionId, string> worktrees = [];
    private readonly Dictionary<SessionId, JobId> jobs = [];
    private readonly Dictionary<JobId, DelegationRecord> pending = [];
    private readonly HashSet<JobId> awaitingHold = [];
    private readonly Dictionary<JobId, (ItemId Item, string Text)> replies = [];
    private bool hydrated;

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();
        worktrees[integrationEvent.Session] = integrationEvent.WorkingDirectory;

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();
        jobs[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();

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
        Hydrate();

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
        else if (Settled.Contains(integrationEvent.Status))
        {
            Settle(integrationEvent.Job, new Settlement(integrationEvent.Status, Option<HoldReason>.None), cancellationToken);
        }
    }

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();

        if (awaitingHold.Remove(integrationEvent.Hold.Job))
        {
            Settle(integrationEvent.Hold.Job, new Settlement(JobStatus.NeedsHelp, integrationEvent.Hold.Reason), cancellationToken);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();

        foreach (var child in pending.Keys.Where(child => !awaitingHold.Contains(child)).ToList())
        {
            var history = await catalog.HistoryAsync(child, cancellationToken);
            var status = history.Map(found => found.Summary.Status);

            if (status.Match(Settled.Contains, () => false))
            {
                var held = status == Option<JobStatus>.Some(JobStatus.NeedsHelp) && history.Match(Interrupted, () => false);
                Settle(child, new Settlement(status.Match(found => found, () => default), Option<HoldReason>.None) { Held = held }, cancellationToken);
            }
        }
    }

    private void Hydrate()
    {
        var earlier = book.Earlier.Match(restored => restored, () => []);

        if (hydrated || book.Earlier.IsNone)
        {
            return;
        }

        hydrated = true;

        foreach (var record in earlier.Where(record => record.Report.IsNone))
        {
            foreach (var child in record.Child.Match<JobId[]>(found => [found], () => []))
            {
                pending.TryAdd(child, record);
            }
        }
    }

    private async Task DelegateAsync(ToolCalled called, CancellationToken cancellationToken)
    {
        var parent = jobs.TryGetValue(called.Session, out var job) ? job : Option<JobId>.None;
        var caller = new Caller(
            called.Session,
            parent,
            worktrees.TryGetValue(called.Session, out var worktree) ? worktree : Option<string>.None,
            pending.Values.Count(waiting => parent.IsSome && waiting.Parent == parent))
        {
            Role = parent.Bind(book.OfChild).Match(record => record.Role, () => ChildRole.Worker),
        };
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
        (await catalog.HistoryAsync(child, cancellationToken)).Match(Interrupted, () => false);

    private static bool Interrupted(JobHistory history) => history.Attempts is [.., { Outcome: AttemptOutcome.Interrupted }];
}
