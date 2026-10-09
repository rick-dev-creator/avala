using Avala.Agents.Contracts;
using Avala.Delegation.Contracts;
using Avala.Sdk.Events;

namespace Avala.Delegation.Records;

internal sealed class DelegationJournal(DelegationBook book, IEventBus bus, IAgents agents, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

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

    public async Task ReportedAsync(DelegationRecord reported, ChildReport report, CancellationToken cancellationToken)
    {
        await book.KeepAsync(reported, cancellationToken);
        var returned = await agents.ReturnAsync(reported.Session, ToolAnswers.Reported(reported.Item, reported, report), cancellationToken);
        var answered = returned.IsSuccess ? reported with { Answered = new CallAnswer(AnswerRoute.ToolResult, Now) } : reported;

        if (returned.IsSuccess)
        {
            await book.KeepAsync(answered, cancellationToken);
        }

        await bus.PublishAsync(new ChildReported(answered), cancellationToken);

        if (returned.IsSuccess)
        {
            await bus.PublishAsync(new ReportDelivered(answered), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DelegationRecord>> BriefedAsync(IReadOnlyList<DelegationRecord> reported, CancellationToken cancellationToken)
    {
        var answered = reported.Select(record => record with { Answered = new CallAnswer(AnswerRoute.Message, Now) }).ToList();

        foreach (var record in answered)
        {
            await book.KeepAsync(record, cancellationToken);
            await bus.PublishAsync(new ReportDelivered(record), cancellationToken);
        }

        return answered;
    }
}
