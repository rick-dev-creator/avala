using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Cards;

internal static class CardPhrases
{
    public const string AlwaysInRepository = "Always in this repository";

    public const string EverythingAllowed = "The rest of this job runs without asking, within the repository's guards.";

    public const string Withdrawn = "Withdrawn by the harness";

    public static string AlwaysInRepositoryScope(PolicyRule rule) =>
        $"Adds a rule for exactly {rule.Target.Match(target => target, () => string.Empty)} to .avala/permissions.json in the repository. It applies to new jobs once committed; this job stops asking now.";

    public static string Remembered(HumanAnswer answer) =>
        answer.Everything ? EverythingAllowed
        : answer.RepositoryRule.Match(
            _ => "Added to .avala/permissions.json. It applies to new jobs once you commit it; this job won't ask again.",
            () => answer.RepositoryError.Match(error => $"Not added to .avala/permissions.json: {NotAdded(error)} This job won't ask again.", () => string.Empty));

    public static string Denied(ItemKind kind, string target) => kind switch
    {
        ItemKind.Command => $"Run {target}",
        ItemKind.FileEdit => $"Edit {target}",
        ItemKind.Web => $"Fetch {target}",
        ItemKind.Mcp => $"Use {target}",
        _ => target,
    };

    public static string WhyDenied(PolicyDecision decision) =>
        decision.Rule.Match(rule => rule.Scope == RuleScope.OutsideWorkspace && decision.Kind == ItemKind.FileEdit, () => false)
            ? $"{decision.Target} is outside the job's worktree, and writing there needs you."
            : decision.Scope.Bind(scope => scope.Rule).IsSome
            ? "Nobody was watching, so the policy denied it."
            : "A guard keeps this for a person, so it can't be allowed while the job runs on its own.";

    public static string AllowedAfterDenial(Option<JobRejection> untold) =>
        untold.Match(
            _ => "Allowed for this job. The agent could not be told now; tell it to retry when you continue.",
            () => "Allowed for this job. Avala told the agent it may retry.");

    public const string WaitingForParent = "Waiting for its parent";

    public static string Verdict(PermissionEntry permission) =>
        permission.Resolution.Match(
            answer => (answer == PermissionAnswer.Allow ? "Allowed" : "Denied") + ByParent(permission.Decision.Map(decision => (decision.Parent, decision.Delivery))),
            () => Waiting(
                permission.Withdrawn,
                permission.Closed,
                permission.AwaitsParent,
                permission.AwaitsHuman));

    public static string Verdict(FormEntry form) =>
        form.Answer.Match(
            answer => Answered(form, answer),
            () => Waiting(form.Withdrawn, form.Closed || form.Outcome.IsSome, form.AwaitsParent, form.AwaitsHuman));

    private static string Answered(FormEntry form, FormAnswer answer)
    {
        var byParent = ByParent(form.Decision.Map(decision => (decision.Parent, decision.Delivery)));

        return answer.Declined ? $"Declined{byParent}"
            : byParent.Length > 0 ? $"Answered{byParent}: {Chosen(answer)}"
            : form.WentToHuman ? $"Answered: {Chosen(answer)}"
            : $"Answered for you: {Chosen(answer)}";
    }

    private static string Waiting(bool withdrawn, bool closed, bool awaitsParent, bool awaitsHuman) =>
        withdrawn ? Withdrawn
        : closed ? "No longer waiting"
        : awaitsParent ? WaitingForParent
        : awaitsHuman ? "Waiting for you"
        : "Deciding";

    private static string ByParent(Option<(Option<JobId> Parent, DecisionDelivery Delivery)> decision) =>
        decision.Match(found => found.Parent.IsSome && found.Delivery == DecisionDelivery.Answered, () => false) ? " by its parent" : string.Empty;

    public static string Writes(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? string.Empty : $"Writes to {string.Join(", ", paths)}";

    public static string Headline(ItemKind kind) => kind switch
    {
        ItemKind.Command => "Wants to run a command",
        ItemKind.FileEdit => "Wants to edit a file",
        ItemKind.Web => "Wants to reach the web",
        ItemKind.Mcp => "Wants to use a tool",
        _ => "Asks permission",
    };

    public static string Headline(FormPurpose purpose) => purpose switch
    {
        FormPurpose.Question => "Asks a question",
        FormPurpose.PlanApproval => "Asks you to approve a plan",
        FormPurpose.Permission => "Asks permission",
        _ => "Asks for input",
    };

    public static string Error(PolicyError error) => error switch
    {
        PolicyError.NotAwaitingAnswer => "The agent no longer waits for this answer.",
        _ => "The answer could not be recorded.",
    };

    public static string Error(AgentError error) => error switch
    {
        AgentError.SessionClosed => "The agent's session has closed.",
        AgentError.NoPendingForm => "The agent no longer waits for this form.",
        AgentError.InvalidAnswer => "The answer does not fit the form.",
        AgentError.Unsupported => "This agent does not take answers to forms.",
        _ => "The answer did not reach the agent.",
    };

    private static string NotAdded(PolicyError error) => error switch
    {
        PolicyError.NotARepositoryRule => "this request cannot be written as one exact rule.",
        PolicyError.RepositoryUnwritable => "the repository's file could not be written.",
        _ => "the repository's file is not valid; fix it in Settings first.",
    };

    private static string Chosen(FormAnswer answer) =>
        string.Join(", ", answer.Fields.SelectMany(field => field.Chosen
            .Concat(field.Text.Match(text => new[] { text }, () => []))
            .Concat(field.Confirmed ? ["confirmed"] : [])));
}
