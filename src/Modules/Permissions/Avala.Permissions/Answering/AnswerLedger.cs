using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk.Events;

namespace Avala.Permissions.Answering;

internal sealed class AnswerLedger(GovernanceBook book, IEventBus bus, TimeProvider clock)
{
    public async Task<HumanAnswer> RecordAsync(HumanAnswer answer, CancellationToken cancellationToken)
    {
        var recorded = answer with { At = clock.GetUtcNow() };
        await book.RecordAsync(recorded, cancellationToken);
        await bus.PublishAsync(new PermissionAnswered(recorded), cancellationToken);

        return recorded;
    }
}
