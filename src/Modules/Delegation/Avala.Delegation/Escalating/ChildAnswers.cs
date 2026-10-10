using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal enum ChildAnswerRefusal
{
    MalformedInput,
    NotWaiting,
    NotYourChild,
    BeyondYourRules,
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
        var permission = audit.OfJob(input.Child).LastOrDefault(decision => decision.Item == input.Request && decision.Delivery == DecisionDelivery.LeftToParent).ToOption();
        var form = audit.FormsOfJob(input.Child).LastOrDefault(decision => decision.Item == input.Request && decision.Delivery == DecisionDelivery.LeftToParent).ToOption();
        var waiting = permission.Map(found => (found.Session, found.Parent)).Match(Option<(SessionId, Option<JobId>)>.Some, () => form.Map(found => (found.Session, found.Parent)));

        if (!waiting.ToResult(ChildAnswerRefusal.NotWaiting).TryGetValue(out var asked, out _))
        {
            return ChildAnswerRefusal.NotWaiting;
        }

        var (child, asksParent) = asked;

        if (caller.IsNone || asksParent != caller)
        {
            return ChildAnswerRefusal.NotYourChild;
        }

        if (input.Decision == ChildDecision.Person)
        {
            return await answers.PassAsync(child, input.Request, PassReason.PassedByParent, cancellationToken) ? input.Decision : ChildAnswerRefusal.NotWaiting;
        }

        var answered = permission.IsSome
            ? (await answers.AnswerAsync(child, new ParentReply(input.Request, parent, input.Decision == ChildDecision.Allow ? PermissionAnswer.Allow : PermissionAnswer.Deny) { Message = input.Message }, cancellationToken))
                .Match(_ => Option<PolicyError>.None, Option<PolicyError>.Some)
            : (await answers.AnswerFormAsync(child, new ParentFormReply(parent, input.FormAnswer), cancellationToken))
                .Match(_ => Option<PolicyError>.None, Option<PolicyError>.Some);

        return answered.Match(
            error => Result<ChildDecision, ChildAnswerRefusal>.Failure(error switch
            {
                PolicyError.BeyondParent => ChildAnswerRefusal.BeyondYourRules,
                PolicyError.InvalidAnswer => ChildAnswerRefusal.InvalidAnswer,
                _ => ChildAnswerRefusal.NotWaiting,
            }),
            () => input.Decision);
    }

    private static ToolResult Result(ItemId item, Result<ChildDecision, ChildAnswerRefusal> outcome) =>
        outcome.Match(
            decision => new ToolResult(item, new JsonObject { ["answered"] = Camel(decision.ToString()) }.ToJsonString()),
            refusal => new ToolResult(item, new JsonObject { ["refused"] = Camel(refusal.ToString()), ["reason"] = Reason(refusal) }.ToJsonString()) { IsError = true });

    private static string Camel(string name) => $"{char.ToLowerInvariant(name[0])}{name[1..]}";

    private static string Reason(ChildAnswerRefusal refusal) => refusal switch
    {
        ChildAnswerRefusal.MalformedInput => "the input needs child and request as the message names them, a decision of allow, deny or person, an optional message, and fields only for a question.",
        ChildAnswerRefusal.NotYourChild => "that request belongs to a sub-agent of another job.",
        ChildAnswerRefusal.BeyondYourRules => "your own rules would not allow you to do this, so you cannot allow it for your sub-agent; it went to a person.",
        ChildAnswerRefusal.InvalidAnswer => "the answer does not fit the question's fields; it still waits for your answer.",
        _ => "that request no longer waits for you: it was answered, passed to a person, or its sub-agent stopped asking.",
    };
}
