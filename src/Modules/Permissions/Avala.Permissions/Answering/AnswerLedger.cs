using Avala.Agents.Contracts.Sessions;
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

    public async Task ReplacedAsync(PolicyDecision decision, CancellationToken cancellationToken)
    {
        await book.ReplacedAsync(decision, cancellationToken);
        await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
    }

    public async Task ReplacedAsync(FormDecision decision, CancellationToken cancellationToken)
    {
        await book.ReplacedAsync(decision, cancellationToken);
        await bus.PublishAsync(new FormDecided(decision), cancellationToken);
    }

    public async Task<bool> PassedAsync(SessionId session, ItemId item, PassReason reason, CancellationToken cancellationToken)
    {
        var permission = await book.PassedAsync(session, item, (PolicyDecision waiting) => waiting with { Delivery = DecisionDelivery.LeftToHuman, Passed = reason }, cancellationToken);
        var form = await book.PassedAsync(session, item, (FormDecision waiting) => waiting with { Delivery = DecisionDelivery.LeftToHuman, Passed = reason }, cancellationToken);

        foreach (var passed in permission.Match<PolicyDecision[]>(found => [found], () => []))
        {
            await bus.PublishAsync(new PermissionDecided(passed), cancellationToken);
        }

        foreach (var passed in form.Match<FormDecision[]>(found => [found], () => []))
        {
            await bus.PublishAsync(new FormDecided(passed), cancellationToken);
        }

        return permission.IsSome || form.IsSome;
    }
}
