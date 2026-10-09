using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Cards;

internal static class CardPhrases
{
    public static string Verdict(PermissionEntry permission) =>
        permission.Resolution.Match(
            answer => answer == PermissionAnswer.Allow ? "Allowed" : "Denied",
            () => permission.Closed ? "No longer waiting" : permission.AwaitsHuman ? "Waiting for you" : "Deciding");

    public static string Verdict(FormEntry form) =>
        form.Answer.Match(
            answer => answer.Declined
                ? "Declined"
                : form.WentToHuman ? $"Answered: {Chosen(answer)}" : $"Answered for you: {Chosen(answer)}",
            () => form.Closed || form.Outcome.IsSome ? "No longer waiting" : form.AwaitsHuman ? "Waiting for you" : "Deciding");

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

    private static string Chosen(FormAnswer answer) =>
        string.Join(", ", answer.Fields.SelectMany(field => field.Chosen
            .Concat(field.Text.Match(text => new[] { text }, () => []))
            .Concat(field.Confirmed ? ["confirmed"] : [])));
}
