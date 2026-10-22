using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed class ParentNotes(DelegationJournal journal, ParentWaking waking) : IHandle<FormDecided>, IHandle<JobProgressed>
{
    private readonly Dictionary<(SessionId Session, ItemId Item), (JobId Parent, ParentAsked Asked)> untold = [];

    public async ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;
        var key = (decision.Session, decision.Item);

        if (decision.Delivery != DecisionDelivery.LeftToParent)
        {
            untold.Remove(key);

            return;
        }

        foreach (var child in decision.Job.Bind(journal.OfChild).Match<DelegationRecord[]>(record => [record], () => []))
        {
            await AskAsync(child, decision, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp))
        {
            return;
        }

        foreach (var (key, waiting) in untold.Where(pending => pending.Value.Parent == integrationEvent.Job).ToList())
        {
            if (await waking.WakeIdleAsync(waiting.Parent, integrationEvent.Status, waiting.Asked.Note, cancellationToken) == ParentReach.Woken)
            {
                untold.Remove(key);
                await journal.AskedAsync(waiting.Asked with { Woken = true }, cancellationToken);
            }
        }
    }

    private async Task AskAsync(DelegationRecord child, FormDecision decision, CancellationToken cancellationToken)
    {
        var asked = new ParentAsked(child, decision.Session, decision.Item, ChildNotes.Asking(decision));
        var job = child.Child.Match(found => found, () => default);

        if (await child.Child.Bind(journal.Calls.Take).Match(
            call => journal.ReturnEarlyAsync(job, call, ChildNotes.FormResult(child, decision), cancellationToken),
            () => Task.FromResult(false)))
        {
            await journal.AskedAsync(asked with { InCall = true }, cancellationToken);

            return;
        }

        journal.Calls.Queue(job, new QueuedAsking(decision.Session, decision.Item, ChildNotes.FormResult(child, decision)));
        var noted = asked with { Note = ChildNotes.Form(child, decision) };

        foreach (var parent in child.Parent.Match<JobId[]>(found => [found], () => []))
        {
            switch (await waking.ReachAsync(parent, job, noted.Note, cancellationToken))
            {
                case ParentReach.Told:
                    await journal.AskedAsync(noted, cancellationToken);
                    break;
                case ParentReach.Woken:
                    await journal.AskedAsync(noted with { Woken = true }, cancellationToken);
                    break;
                case ParentReach.Later:
                    untold[(decision.Session, decision.Item)] = (parent, noted);
                    break;
            }
        }
    }
}
