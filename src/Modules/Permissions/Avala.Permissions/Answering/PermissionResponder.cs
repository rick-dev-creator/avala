using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class PermissionResponder(IAgents agents, IRealPaths paths, TimeProvider clock)
{
    public async Task<PolicyDecision> DecideAsync(
        GovernedSession session,
        PermissionRequested requested,
        IReadOnlyList<PolicyRule> sessionRules,
        CancellationToken cancellationToken)
    {
        var request = requested.Facts(session.WorkingDirectory, paths);
        var verdict = session.Policy.Decide(request, sessionRules);
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
            clock.GetUtcNow())
        {
            Autonomy = session.Policy.Autonomy,
        };
    }

    public async Task<FormDecision> DecideAsync(GovernedSession session, FormRequested requested, CancellationToken cancellationToken)
    {
        var automatic = session.Policy.Answer(requested.Item, requested.Form);
        var delivery = await automatic.Match(
            async answered => (await agents.AnswerAsync(requested.Session, answered.Answer, cancellationToken)).Match(
                _ => DecisionDelivery.Answered,
                _ => DecisionDelivery.Undelivered),
            () => Task.FromResult(DecisionDelivery.LeftToHuman));

        return new FormDecision(
            requested.Session,
            requested.Turn,
            requested.Item,
            session.Job,
            requested.Form,
            session.Policy.Autonomy,
            automatic.Map(answered => answered.Answer),
            automatic.Match(answered => answered.Assumptions, () => []),
            delivery,
            clock.GetUtcNow());
    }

    private async Task<DecisionDelivery> RespondAsync(PermissionRequested requested, PermissionAnswer answer, CancellationToken cancellationToken) =>
        (await agents.RespondAsync(requested.Session, new PermissionDecision(requested.Item, answer), cancellationToken)).Match(
            _ => DecisionDelivery.Answered,
            _ => DecisionDelivery.Undelivered);
}
