using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed record ChildQuestion(SessionId Session, ItemId Item, Option<JobId> Child, bool Escalates, string Asking, Func<DelegationRecord, ParentEscalation, string> Note);

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
                (child, escalation) => ChildNotes.Permission(child, decision, escalation)),
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
                (child, escalation) => ChildNotes.Form(child, decision, escalation)),
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

    private Task<Option<ParentAsked>> TellAsync(DelegationRecord child, ChildQuestion question, CancellationToken cancellationToken) =>
        child.Parent.Bind(parent => child.Escalation.Map(escalation => (Parent: parent, Escalation: escalation))).Match(
            async found =>
            {
                var until = clock.GetUtcNow() + found.Escalation.Window;
                var note = question.Note(child, found.Escalation);

                return (await jobs.SteerAsync(found.Parent, note, cancellationToken)).IsSuccess
                    ? new ParentAsked(child, question.Session, question.Item, question.Asking, until) { Note = note }
                    : Option<ParentAsked>.None;
            },
            () => Task.FromResult(Option<ParentAsked>.None));
}
