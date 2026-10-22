using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed record UntoldQuestion(JobId Parent, JobId Child, ParentAsked Asked);

internal sealed class ParentNotes(DelegationJournal journal, ParentWaking waking) : IHandle<FormDecided>, IHandle<JobProgressed>
{
    private readonly Dictionary<(SessionId Session, ItemId Item), UntoldQuestion> untold = [];

    public async ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;

        if (decision.Delivery != DecisionDelivery.LeftToParent)
        {
            untold.Remove((decision.Session, decision.Item));

            return;
        }

        foreach (var child in decision.Job.Bind(journal.OfChild).Match<DelegationRecord[]>(record => [record], () => []))
        {
            await AskAsync(child, decision, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            foreach (var ended in untold.Where(pending => pending.Value.Child == integrationEvent.Job).Select(pending => pending.Key).ToList())
            {
                untold.Remove(ended);
            }

            return;
        }

        var waiting = untold.Where(pending => pending.Value.Parent == integrationEvent.Job).ToList();

        if (waiting.Count == 0 || integrationEvent.Status is not (JobStatus.Running or JobStatus.AwaitingReview or JobStatus.NeedsHelp))
        {
            return;
        }

        var note = string.Join("\n\n", waiting.Select(pending => pending.Value.Asked.Note));
        var reached = integrationEvent.Status == JobStatus.Running
            ? await waking.TellAsync(integrationEvent.Job, note, cancellationToken) ? ParentReach.Told : ParentReach.Later
            : await waking.WakeIdleAsync(integrationEvent.Job, note, cancellationToken);

        if (reached == ParentReach.Later)
        {
            return;
        }

        foreach (var (key, question) in waiting)
        {
            untold.Remove(key);
            await journal.AskedAsync(question.Asked with { Woken = reached == ParentReach.Woken }, cancellationToken);
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
            var reached = await waking.ReachAsync(parent, job, noted.Note, cancellationToken);

            if (reached == ParentReach.Later)
            {
                untold[(decision.Session, decision.Item)] = new UntoldQuestion(parent, job, noted);
            }

            if (reached != ParentReach.Gone)
            {
                await journal.AskedAsync(noted with { Woken = reached == ParentReach.Woken }, cancellationToken);
            }
        }
    }
}
