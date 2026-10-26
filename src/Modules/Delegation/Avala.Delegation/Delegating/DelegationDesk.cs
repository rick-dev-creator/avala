using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Delegating;

internal sealed class DelegationDesk(Delegator delegator, ChildReporter reporter, IJobCatalog catalog, DelegationBook book)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<JobProgressed>, IHandle<JobHeld>, IHandle<StartupCompleted>, IHandle<ChildWentOn>
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
            case TurnCompleted { Outcome: TurnOutcome.Finished } finished when IsChild(finished.Session, out var recovered):
                await RecoveredAsync(recovered, cancellationToken);
                break;
        }
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();

        if (Ended(integrationEvent.Status))
        {
            foreach (var orphan in book.OfParent(integrationEvent.Job).Where(StillOpen).SelectMany(record => record.Child.Match<JobId[]>(child => [child], () => [])))
            {
                reporter.Discard(orphan, cancellationToken);
            }
        }

        if (!pending.ContainsKey(integrationEvent.Job))
        {
            return;
        }

        if (integrationEvent.Status == JobStatus.NeedsHelp)
        {
            await NeedsHelpAsync(integrationEvent, cancellationToken);
        }
        else if (Settled.Contains(integrationEvent.Status))
        {
            Settle(integrationEvent.Job, new Settlement(integrationEvent.Status, Option<HoldReason>.None) { Failure = integrationEvent.Failure }, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();
        var hold = integrationEvent.Hold;

        if (!awaitingHold.Remove(hold.Job) || !pending.TryGetValue(hold.Job, out var delegation))
        {
            return;
        }

        if (hold.Reason == HoldReason.LimitNearlyReached)
        {
            reporter.Held(delegation, hold.Job, hold, cancellationToken);
        }
        else if (ChildRetries.Retries(hold.Reason))
        {
            await RetryAsync(delegation, hold, cancellationToken);
        }
        else
        {
            Settle(hold.Job, new Settlement(JobStatus.NeedsHelp, hold.Reason), cancellationToken);
        }
    }

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();

        foreach (var child in pending.Keys.Where(child => !awaitingHold.Contains(child)).ToList())
        {
            await SettleIfSettledAsync(child, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(ChildWentOn integrationEvent, CancellationToken cancellationToken)
    {
        Hydrate();
        pending.TryAdd(integrationEvent.Child, integrationEvent.Delegation);

        foreach (var summary in integrationEvent.Summary.Match<string[]>(text => [text], () => []))
        {
            replies.TryAdd(integrationEvent.Child, (new ItemId(string.Empty), summary));
        }
        await SettleIfSettledAsync(integrationEvent.Child, cancellationToken);
    }

    private async Task NeedsHelpAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var history = await catalog.HistoryAsync(integrationEvent.Job, cancellationToken);

        if (history.Match(MovedOn, () => false))
        {
            return;
        }

        if (history.Match(Interrupted, () => false))
        {
            awaitingHold.Add(integrationEvent.Job);
        }
        else
        {
            Settle(integrationEvent.Job, new Settlement(JobStatus.NeedsHelp, Option<HoldReason>.None) { Failure = integrationEvent.Failure }, cancellationToken);
        }
    }

    private async Task SettleIfSettledAsync(JobId child, CancellationToken cancellationToken)
    {
        var history = await catalog.HistoryAsync(child, cancellationToken);
        var status = history.Map(found => found.Summary.Status);

        if (!status.Match(Settled.Contains, () => false))
        {
            return;
        }

        var held = status == Option<JobStatus>.Some(JobStatus.NeedsHelp) && history.Match(Interrupted, () => false);
        var reason = held ? history.Bind(found => found.Summary.Hold) : Option<HoldReason>.None;

        if (reason.Match(ChildRetries.Retries, () => false) && pending.TryGetValue(child, out var delegation))
        {
            await RetryAsync(delegation, new JobHold(child, Option<SessionId>.None, reason.Match(found => found, () => default), SessionHalt.Interrupted), cancellationToken);

            return;
        }

        Settle(child, new Settlement(status.Match(found => found, () => default), Option<HoldReason>.None) { Held = held }, cancellationToken);
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

    private async Task RecoveredAsync(JobId child, CancellationToken cancellationToken)
    {
        if (pending.TryGetValue(child, out var delegation) && delegation.Retries > 0)
        {
            var recovered = delegation with { Retries = 0 };
            pending[child] = recovered;
            await book.KeepAsync(recovered, cancellationToken);
        }
    }

    private async Task RetryAsync(DelegationRecord delegation, JobHold hold, CancellationToken cancellationToken)
    {
        var givenUp = new Settlement(JobStatus.NeedsHelp, hold.Reason) { GivenUp = true };

        if (delegation.Retries >= ChildRetries.Most)
        {
            Settle(hold.Job, givenUp, cancellationToken);

            return;
        }

        var retried = delegation with { Retries = delegation.Retries + 1 };
        var refusal = await reporter.RetryAsync(retried, hold.Job, hold, cancellationToken);

        if (refusal.IsNone)
        {
            pending[hold.Job] = retried;
            await book.KeepAsync(retried, cancellationToken);
        }
        else if (refusal != Option<JobRejection>.Some(JobRejection.NotHeld))
        {
            Settle(hold.Job, givenUp, cancellationToken);
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

    private static bool Ended(JobStatus status) => status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed;

    private static bool StillOpen(DelegationRecord record) => record.Report.Match(report => !Ended(report.Status), () => true);

    private static bool Interrupted(JobHistory history) => history.Attempts is [.., { Outcome: AttemptOutcome.Interrupted }];

    private static bool MovedOn(JobHistory history) => history.Summary.Status != JobStatus.NeedsHelp;
}
