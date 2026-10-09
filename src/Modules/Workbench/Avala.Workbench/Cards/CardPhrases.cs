using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Cards;

internal static class CardPhrases
{
    public const string DontAskAgain = "Don't ask again this session";

    public const string Withdrawn = "Withdrawn by the harness";

    public static string DontAskAgainScope(ItemKind kind) =>
        $"Your answer is reused only for this exact {Requested(kind)} and only until this agent session ends: a new session of the job, after a hold or a restart, asks again.";

    public static string Verdict(PermissionEntry permission) =>
        permission.Resolution.Match(
            answer => answer == PermissionAnswer.Allow ? "Allowed" : "Denied",
            () => permission.Withdrawn ? Withdrawn : permission.Closed ? "No longer waiting" : permission.AwaitsHuman ? "Waiting for you" : "Deciding");

    public static string Verdict(FormEntry form) =>
        form.Answer.Match(
            answer => answer.Declined
                ? "Declined"
                : form.WentToHuman ? $"Answered: {Chosen(answer)}" : $"Answered for you: {Chosen(answer)}",
            () => form.Withdrawn ? Withdrawn : form.Closed || form.Outcome.IsSome ? "No longer waiting" : form.AwaitsHuman ? "Waiting for you" : "Deciding");

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
