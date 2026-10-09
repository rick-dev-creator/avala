using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk.Events;

namespace Avala.Permissions.Answering;

internal sealed class PermissionResponder(GovernanceBook book, IAgents agents, IEventBus bus, TimeProvider clock)
    : IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not PermissionRequested requested)
        {
            return;
        }

        var session = book.Of(requested.Session);
        var request = requested.Facts(session.WorkingDirectory);
        var verdict = session.Policy.Decide(request);
        var delivery = verdict.Answer switch
        {
            PolicyAnswer.Allow => await RespondAsync(requested, PermissionAnswer.Allow, cancellationToken),
            PolicyAnswer.Deny => await RespondAsync(requested, PermissionAnswer.Deny, cancellationToken),
            _ => DecisionDelivery.LeftToHuman,
        };

        var decision = new PolicyDecision(
            requested.Session,
            requested.Turn,
            requested.Item,
            session.Job,
            request.Kind,
            request.Target,
            verdict.Answer,
            verdict.Rule,
            delivery,
            clock.GetUtcNow());

        book.Keep(book.Of(requested.Session).Decided(decision));
        await bus.PublishAsync(new PermissionDecided(decision), cancellationToken);
    }

    private async Task<DecisionDelivery> RespondAsync(PermissionRequested requested, PermissionAnswer answer, CancellationToken cancellationToken) =>
        (await agents.RespondAsync(requested.Session, new PermissionDecision(requested.Item, answer), cancellationToken)).Match(
            _ => DecisionDelivery.Answered,
            _ => DecisionDelivery.Undelivered);
}
