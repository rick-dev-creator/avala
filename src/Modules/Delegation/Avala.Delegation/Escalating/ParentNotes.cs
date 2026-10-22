using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed class ParentNotes(DelegationJournal journal, ParentWaking waking, UntoldQuestions untold) : IHandle<FormDecided>, IHandle<JobProgressed>
{
    public async ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;

        if (decision.Delivery != DecisionDelivery.LeftToParent)
        {
            untold.Forget(decision.Session, decision.Item);

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
            _ = untold.Claim(question => question.Child == integrationEvent.Job);

            return;
        }

        if (integrationEvent.Status != JobStatus.AwaitingReview)
        {
            return;
        }

        var waiting = untold.Claim(question => question.Parent == integrationEvent.Job);

        if (waiting.Count == 0)
        {
            return;
        }

        if (await waking.WakeIdleAsync(integrationEvent.Job, UntoldQuestions.Folded(waiting), cancellationToken) == ParentReach.Woken)
        {
            await untold.ToldAsync(waiting, woken: true, cancellationToken);

            return;
        }

        untold.Release(waiting);
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
                untold.Keep(new UntoldQuestion(parent, job, noted));
            }

            if (reached != ParentReach.Gone)
            {
                await journal.AskedAsync(noted with { Woken = reached == ParentReach.Woken }, cancellationToken);
            }
        }
    }
}
