using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;

namespace Avala.Permissions.Answering;

internal sealed class PermissionResponder(IAgents agents, TimeProvider clock)
{
    public async Task<PolicyDecision> DecideAsync(GovernedSession session, PermissionRequested requested, CancellationToken cancellationToken)
    {
        var request = requested.Facts(session.WorkingDirectory);
        var verdict = session.Policy.Decide(request);
        var delivery = verdict.Answer switch
        {
            PolicyAnswer.Allow => await RespondAsync(requested, PermissionAnswer.Allow, cancellationToken),
            PolicyAnswer.Deny => await RespondAsync(requested, PermissionAnswer.Deny, cancellationToken),
            _ => DecisionDelivery.LeftToHuman,
        };

        return new PolicyDecision(
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
    }

    private async Task<DecisionDelivery> RespondAsync(PermissionRequested requested, PermissionAnswer answer, CancellationToken cancellationToken) =>
        (await agents.RespondAsync(requested.Session, new PermissionDecision(requested.Item, answer), cancellationToken)).Match(
            _ => DecisionDelivery.Answered,
            _ => DecisionDelivery.Undelivered);
}
