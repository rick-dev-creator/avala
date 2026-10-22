using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal enum ChildAnswerRefusal
{
    MalformedInput,
    NotWaiting,
    NotYourChild,
    InvalidAnswer,
}

internal sealed class ChildAnswers(IParentAnswers answers, IPermissionAudit audit, IAgents agents) : IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is ToolCalled { Tool: AnswerChildTool.Name } called)
        {
            var outcome = await AnswerChildInput.Parse(called.Input).Match(
                input => AnswerAsync(called.Session, input, cancellationToken),
                () => Task.FromResult(Result<ChildDecision, ChildAnswerRefusal>.Failure(ChildAnswerRefusal.MalformedInput)));
            _ = await agents.ReturnAsync(called.Session, Result(called.Item, outcome), cancellationToken);
        }
    }

    private async Task<Result<ChildDecision, ChildAnswerRefusal>> AnswerAsync(SessionId parent, AnswerChildInput input, CancellationToken cancellationToken)
    {
        var caller = audit.AutonomyOf(parent).Map(applied => applied.Job);
        var waiting = audit.FormsOfJob(input.Child).LastOrDefault(decision => decision.Item == input.Request && decision.Delivery == DecisionDelivery.LeftToParent).ToOption();

        if (!waiting.ToResult(ChildAnswerRefusal.NotWaiting).TryGetValue(out var asked, out _))
        {
            return ChildAnswerRefusal.NotWaiting;
        }

        if (caller.IsNone || asked.Parent != caller)
        {
            return ChildAnswerRefusal.NotYourChild;
        }

        return (await answers.AnswerFormAsync(asked.Session, new ParentFormReply(parent, input.FormAnswer), cancellationToken)).Match(
            _ => Result<ChildDecision, ChildAnswerRefusal>.Success(input.Decision),
            error => error == PolicyError.InvalidAnswer ? ChildAnswerRefusal.InvalidAnswer : ChildAnswerRefusal.NotWaiting);
    }

    private static ToolResult Result(ItemId item, Result<ChildDecision, ChildAnswerRefusal> outcome) =>
        outcome.Match(
            decision => new ToolResult(item, new JsonObject { ["answered"] = Camel(decision.ToString()) }.ToJsonString()),
            refusal => new ToolResult(item, new JsonObject { ["refused"] = Camel(refusal.ToString()), ["reason"] = Reason(refusal) }.ToJsonString()) { IsError = true });

    private static string Camel(string name) => $"{char.ToLowerInvariant(name[0])}{name[1..]}";

    private static string Reason(ChildAnswerRefusal refusal) => refusal switch
    {
        ChildAnswerRefusal.MalformedInput => "the input needs child and request as the message names them, a decision of allow or deny, an optional message, and one answer per field to allow.",
        ChildAnswerRefusal.NotYourChild => "that question belongs to a sub-agent of another job.",
        ChildAnswerRefusal.InvalidAnswer => "the answer does not fit the question's fields; it still waits for your answer.",
        _ => "that question no longer waits for you: it was answered, or its sub-agent stopped asking.",
    };
}
