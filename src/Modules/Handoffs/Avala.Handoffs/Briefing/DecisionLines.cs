using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Handoffs.Briefing;

internal static class DecisionLines
{
    public static IReadOnlyList<string> Taken(IPermissionAudit audit, JobId job) =>
    [
        .. audit.OfJob(job).Where(decision => decision.Delivery == DecisionDelivery.Answered).Select(Decided),
        .. audit.AnswersOfJob(job).Select(answer => $"A person {(answer.Answer == PermissionAnswer.Allow ? "allowed" : "denied")} {answer.Kind} {answer.Target}"),
        .. audit.FormsOfJob(job).Where(form => form.Answer.IsSome).Select(form => $"Answered for the agent: {form.Form.Title}"),
    ];

    public static IReadOnlyList<string> Open(IPermissionAudit audit, JobId job)
    {
        var answers = audit.AnswersOfJob(job);

        return
        [
            .. audit.OfJob(job)
                .Where(decision => decision.Delivery == DecisionDelivery.LeftToHuman && !answers.Any(answer => answer.Session == decision.Session && answer.Item == decision.Item))
                .Select(decision => $"Unanswered permission: {decision.Kind} {decision.Target}"),
            .. audit.FormsOfJob(job).Where(form => form.Answer.IsNone && form.Delivery == DecisionDelivery.LeftToHuman).Select(form => $"{form.Form.Title}: {form.Form.Context}"),
        ];
    }

    private static string Decided(PolicyDecision decision) =>
        $"{(decision.Answer == PolicyAnswer.Deny ? "Denied" : "Allowed")} {decision.Kind} {decision.Target}"
        + decision.Rule.Match(rule => $" by the rule {rule.Name}", () => string.Empty);
}
