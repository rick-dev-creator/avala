using Avala.Agents.Contracts;
using Avala.Delegation.Contracts;
using Avala.Sdk.Events;

namespace Avala.Delegation.Records;

internal sealed class DelegationJournal(DelegationBook book, IEventBus bus, IAgents agents, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task RefusedAsync(DelegationRecord refused, DelegationError error, CancellationToken cancellationToken)
    {
        book.Keep(refused);
        _ = await agents.ReturnAsync(refused.Session, ToolAnswers.Refused(refused.Item, error), cancellationToken);
        await bus.PublishAsync(new DelegationRefused(refused), cancellationToken);
    }

    public async Task DelegatedAsync(DelegationRecord delegated, CancellationToken cancellationToken)
    {
        book.Keep(delegated);
        await bus.PublishAsync(new ChildDelegated(delegated), cancellationToken);
    }

    public async Task ReportedAsync(DelegationRecord reported, ChildReport report, CancellationToken cancellationToken)
    {
        book.Keep(reported);
        _ = await agents.ReturnAsync(reported.Session, ToolAnswers.Reported(reported.Item, reported, report), cancellationToken);
        await bus.PublishAsync(new ChildReported(reported), cancellationToken);
    }
}
