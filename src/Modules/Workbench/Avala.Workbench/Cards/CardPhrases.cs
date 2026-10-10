using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Cards;

internal static class CardPhrases
{
    public const string DontAskAgain = "Don't ask again for this job";

    public const string AlwaysInRepository = "Always in this repository";

    public const string Withdrawn = "Withdrawn by the harness";

    public static string DontAskAgainScope(ItemKind kind) =>
        $"Your answer is reused only for this exact {Requested(kind)}, in every session of this job, after a restart, a retry or a send back too, until the job ends. Other jobs still ask.";

    public static string AlwaysInRepositoryScope(PolicyRule rule) =>
        $"Adds a rule for exactly {rule.Target.Match(target => target, () => string.Empty)} to .avala/permissions.json in the repository. It applies to new jobs once committed; this job stops asking now.";

    public static string Remembered(HumanAnswer answer) =>
        answer.RepositoryRule.Match(
            _ => "Added to .avala/permissions.json. It applies to new jobs once you commit it; this job won't ask again.",
            () => answer.RepositoryError.Match(error => $"Not added to .avala/permissions.json: {NotAdded(error)} This job won't ask again.", () => string.Empty));

    public const string WaitingForParent = "Waiting for its parent";

    public static string Verdict(PermissionEntry permission) =>
        permission.Resolution.Match(
            answer => (answer == PermissionAnswer.Allow ? "Allowed" : "Denied") + ByParent(permission.Decision.Map(decision => (decision.Parent, decision.Delivery))),
            () => Waiting(
                permission.Withdrawn,
                permission.Closed,
                permission.AwaitsParent,
                permission.AwaitsHuman,
                permission.Decision.Bind(decision => decision.Passed)));

    public static string Verdict(FormEntry form) =>
        form.Answer.Match(
            answer => Answered(form, answer),
            () => Waiting(form.Withdrawn, form.Closed || form.Outcome.IsSome, form.AwaitsParent, form.AwaitsHuman, form.Decision.Bind(decision => decision.Passed)));

    private static string Answered(FormEntry form, FormAnswer answer)
    {
        var byParent = ByParent(form.Decision.Map(decision => (decision.Parent, decision.Delivery)));

        return answer.Declined ? $"Declined{byParent}"
            : byParent.Length > 0 ? $"Answered{byParent}: {Chosen(answer)}"
            : form.WentToHuman ? $"Answered: {Chosen(answer)}"
            : $"Answered for you: {Chosen(answer)}";
    }

    private static string Waiting(bool withdrawn, bool closed, bool awaitsParent, bool awaitsHuman, Option<PassReason> passed) =>
        withdrawn ? Withdrawn
        : closed ? "No longer waiting"
        : awaitsParent ? WaitingForParent
        : awaitsHuman ? WaitingForYou(passed)
        : "Deciding";

    public static string Passed(PassReason reason) => reason switch
    {
        PassReason.PassedByParent => "its parent passed it to you",
        PassReason.BeyondParent => "its parent's own rules do not allow it",
        PassReason.ParentTimedOut => "its parent did not answer in time",
        _ => "its parent could not be asked",
    };

    private static string WaitingForYou(Option<PassReason> passed) =>
        passed.Match(reason => $"Waiting for you · {Passed(reason)}", () => "Waiting for you");

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

    private static string Requested(ItemKind kind) => kind switch
    {
        ItemKind.Command => "command",
        ItemKind.FileEdit => "file",
        ItemKind.Web => "address",
        ItemKind.Mcp => "tool call",
        _ => "request",
    };

    private static string Chosen(FormAnswer answer) =>
        string.Join(", ", answer.Fields.SelectMany(field => field.Chosen
            .Concat(field.Text.Match(text => new[] { text }, () => []))
            .Concat(field.Confirmed ? ["confirmed"] : [])));
}
