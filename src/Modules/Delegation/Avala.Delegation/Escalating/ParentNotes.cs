using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed record ChildQuestion(SessionId Session, ItemId Item, Option<JobId> Child, bool Escalates, string Asking, Func<DelegationRecord, ParentEscalation, string> Note)
{
    public Func<DelegationRecord, ParentEscalation, ToolResult> Result { get; init; } = (child, _) => new ToolResult(child.Item, string.Empty);
}

internal sealed class ParentNotes(DelegationJournal journal, IJobs jobs, IParentAnswers answers, TimeProvider clock)
    : IHandle<PermissionDecided>, IHandle<FormDecided>, IAsyncDisposable
{
    private readonly Dictionary<(SessionId Session, ItemId Item), ITimer> windows = [];
    private readonly SerialExecutor passing = new();

    public ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;

        return DecidedAsync(
            new ChildQuestion(
                decision.Session,
                decision.Item,
                decision.Job,
                Escalates(decision.Delivery, decision.Passed),
                ChildNotes.Asking(decision),
                (child, escalation) => ChildNotes.Permission(child, decision, escalation))
            {
                Result = (child, escalation) => ChildNotes.PermissionResult(child, decision, escalation),
            },
            cancellationToken);
    }

    public ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        var decision = integrationEvent.Decision;

        return DecidedAsync(
            new ChildQuestion(
                decision.Session,
                decision.Item,
                decision.Job,
                Escalates(decision.Delivery, decision.Passed),
                ChildNotes.Asking(decision),
                (child, escalation) => ChildNotes.Form(child, decision, escalation))
            {
                Result = (child, escalation) => ChildNotes.FormResult(child, decision, escalation),
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var window in windows.Values)
        {
            await window.DisposeAsync();
        }

        windows.Clear();
        await passing.DisposeAsync();
    }

    private static bool Escalates(DecisionDelivery delivery, Option<PassReason> passed) => delivery == DecisionDelivery.LeftToParent && passed.IsNone;

    private async ValueTask DecidedAsync(ChildQuestion question, CancellationToken cancellationToken)
    {
        var key = (question.Session, question.Item);

        if (!question.Escalates)
        {
            if (windows.Remove(key, out var closed))
            {
                await closed.DisposeAsync();
            }

            return;
        }

        var told = await question.Child.Bind(journal.OfChild).Match(
            record => TellAsync(record, question, cancellationToken),
            () => Task.FromResult(Option<ParentAsked>.None));

        await told.Match(
            async asked =>
            {
                windows[key] = clock.CreateTimer(
                    state => _ = passing.RunAsync(token => answers.PassAsync(question.Session, question.Item, PassReason.ParentTimedOut, token).AsTask(), CancellationToken.None),
                    null,
                    asked.Until - clock.GetUtcNow(),
                    Timeout.InfiniteTimeSpan);
                await journal.AskedAsync(asked, cancellationToken);
            },
            async () => _ = await answers.PassAsync(question.Session, question.Item, PassReason.ParentUnreachable, cancellationToken));
    }

    private async Task<bool> InCallAsync(JobId child, CallRef call, ToolResult asking, CancellationToken cancellationToken)
    {
        journal.Calls.Expect(child);

        if (!await journal.ReturnAsync(call, asking, cancellationToken))
        {
            return false;
        }

        foreach (var (sibling, waiting) in journal.Calls.TakeAll(call.Session))
        {
            foreach (var record in journal.OfChild(sibling).Match<DelegationRecord[]>(found => [found], () => []))
            {
                journal.Calls.Expect(sibling);
                _ = await journal.ReturnAsync(waiting, ToolAnswers.Running(waiting.Item, record), cancellationToken);
            }
        }

        return true;
    }

    private Task<Option<ParentAsked>> TellAsync(DelegationRecord child, ChildQuestion question, CancellationToken cancellationToken) =>
        child.Parent.Bind(parent => child.Escalation.Map(escalation => (Parent: parent, Escalation: escalation))).Match(
            async found =>
            {
                var until = clock.GetUtcNow() + found.Escalation.Window;
                var asked = new ParentAsked(child, question.Session, question.Item, question.Asking, until);

                if (await child.Child.Bind(journal.Calls.Take).Match(
                    call => InCallAsync(child.Child.Match(job => job, () => default), call, question.Result(child, found.Escalation), cancellationToken),
                    () => Task.FromResult(false)))
                {
                    return asked with { InCall = true };
                }

                foreach (var waiting in child.Child.Match<JobId[]>(job => [job], () => []))
                {
                    journal.Calls.Queue(waiting, new QueuedAsking(question.Session, question.Item, question.Result(child, found.Escalation)));
                }

                var note = question.Note(child, found.Escalation);

                return (await jobs.SteerAsync(found.Parent, note, cancellationToken)).IsSuccess
                    ? asked with { Note = note }
                    : Option<ParentAsked>.None;
            },
            () => Task.FromResult(Option<ParentAsked>.None));
}
