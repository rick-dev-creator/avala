using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Records;

internal sealed class DelegationJournal(DelegationBook book, IEventBus bus, IAgents agents, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public OpenCalls Calls { get; } = new();

    public Option<DelegationRecord> OfChild(JobId child) => book.OfChild(child);

    public async Task AskedAsync(ParentAsked asked, CancellationToken cancellationToken) => await bus.PublishAsync(asked, cancellationToken);

    public async Task<bool> ReturnAsync(CallRef call, ToolResult result, CancellationToken cancellationToken) =>
        (await agents.ReturnAsync(call.Session, result with { Item = call.Item }, cancellationToken)).IsSuccess;

    public async Task<DelegationRecord> DeliveredAsync(DelegationRecord reported, ChildReport report, CallRef call, CancellationToken cancellationToken)
    {
        if (!await ReturnAsync(call, ToolAnswers.Reported(call.Item, reported, report), cancellationToken))
        {
            return reported;
        }

        var answer = new CallAnswer(AnswerRoute.ToolResult, Now);
        var answered = reported with { Answered = answer };

        if (await book.AnswerAsync(reported, answer, cancellationToken))
        {
            await bus.PublishAsync(new ReportDelivered(answered), cancellationToken);
        }

        return answered;
    }

    public async Task RefusedAsync(DelegationRecord refused, DelegationError error, CancellationToken cancellationToken)
    {
        await book.KeepAsync(refused, cancellationToken);
        _ = await agents.ReturnAsync(refused.Session, ToolAnswers.Refused(refused.Item, error), cancellationToken);
        await bus.PublishAsync(new DelegationRefused(refused), cancellationToken);
    }

    public async Task DelegatedAsync(DelegationRecord delegated, CancellationToken cancellationToken)
    {
        await book.KeepAsync(delegated, cancellationToken);
        await bus.PublishAsync(new ChildDelegated(delegated), cancellationToken);
    }

    public async Task<DelegationRecord> ReportedAsync(DelegationRecord reported, ChildReport report, CancellationToken cancellationToken)
    {
        await book.KeepAsync(reported, cancellationToken);
        var call = reported.Child.Bind(Calls.Take).Match(open => open, () => new CallRef(reported.Session, reported.Item));
        var returned = await agents.ReturnAsync(call.Session, ToolAnswers.Reported(call.Item, reported, report), cancellationToken);
        var answer = new CallAnswer(AnswerRoute.ToolResult, Now);
        var delivered = returned.IsSuccess && await book.AnswerAsync(reported, answer, cancellationToken);
        var answered = returned.IsSuccess ? reported with { Answered = answer } : reported;

        await bus.PublishAsync(new ChildReported(answered), cancellationToken);

        if (delivered)
        {
            await bus.PublishAsync(new ReportDelivered(answered), cancellationToken);
        }

        return answered;
    }

    public Task<IReadOnlyList<DelegationRecord>> ClaimedAsync(IReadOnlyList<DelegationRecord> reported, CancellationToken cancellationToken) =>
        book.ClaimAsync(reported, new CallAnswer(AnswerRoute.Message, Now), cancellationToken);

    public async Task ConfirmedAsync(IReadOnlyList<DelegationRecord> claimed, CancellationToken cancellationToken)
    {
        foreach (var record in book.Confirm(claimed))
        {
            await bus.PublishAsync(new ReportDelivered(record), cancellationToken);
        }
    }

    public async Task SteeredAsync(DelegationRecord reported, CancellationToken cancellationToken)
    {
        var answer = new CallAnswer(AnswerRoute.Message, Now);

        if (await book.AnswerAsync(reported, answer, cancellationToken))
        {
            await bus.PublishAsync(new ReportDelivered(reported with { Answered = answer }), cancellationToken);
        }
    }

    public bool Unanswered(DelegationRecord record) => book.Unanswered(record);

    public Task ReleasedAsync(IReadOnlyList<DelegationRecord> claimed, CancellationToken cancellationToken) => book.ReleaseAsync(claimed, cancellationToken);
}
