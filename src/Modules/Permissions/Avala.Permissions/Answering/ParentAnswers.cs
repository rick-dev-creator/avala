using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class ParentAnswers(GovernanceBook book, IAgents agents, AnswerLedger ledger) : IParentAnswers
{
    public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId child, ParentReply reply, CancellationToken cancellationToken)
    {
        var governed = book.Of(child);
        var parent = book.Of(reply.Parent);

        if (!governed.WaitingParent(reply.Item).ToResult(PolicyError.NotAwaitingAnswer).TryGetValue(out var asked, out _) || !Asks(asked.Parent, parent))
        {
            return PolicyError.NotAwaitingAnswer;
        }

        if (reply.Answer == PermissionAnswer.Allow && !AllowedTo(parent, governed.Escalated.GetValueOrDefault(reply.Item)))
        {
            await ledger.PassedAsync(child, reply.Item, PassReason.BeyondParent, cancellationToken);

            return PolicyError.BeyondParent;
        }

        if (!book.Claim(child, reply.Item)
            || (await agents.RespondAsync(child, new PermissionDecision(reply.Item, reply.Answer) { Message = reply.Message }, cancellationToken)).IsFailure)
        {
            return PolicyError.NotAwaitingAnswer;
        }

        await ledger.ReplacedAsync(asked with { Delivery = DecisionDelivery.Answered }, cancellationToken);

        return await ledger.RecordAsync(
            new HumanAnswer(child, governed.Job, reply.Item, asked.Kind, asked.Target, reply.Answer, reply.Message, Option<PolicyRule>.None, default) { Parent = parent.Job },
            cancellationToken);
    }

    public async ValueTask<Result<FormDecision, PolicyError>> AnswerFormAsync(SessionId child, ParentFormReply reply, CancellationToken cancellationToken)
    {
        var item = reply.Answer.Item;

        if (!book.Of(child).FormWaitingParent(item).ToResult(PolicyError.NotAwaitingAnswer).TryGetValue(out var asked, out _) || !Asks(asked.Parent, book.Of(reply.Parent)))
        {
            return PolicyError.NotAwaitingAnswer;
        }

        if (asked.Form.Purpose == FormPurpose.Permission && !reply.Answer.Declined)
        {
            await ledger.PassedAsync(child, item, PassReason.BeyondParent, cancellationToken);

            return PolicyError.BeyondParent;
        }

        if (!book.Claim(child, item))
        {
            return PolicyError.NotAwaitingAnswer;
        }

        var delivered = await agents.AnswerAsync(child, reply.Answer, cancellationToken);

        if (!delivered.TryGetValue(out _, out var error))
        {
            book.Reopen(child, item);

            return error == AgentError.InvalidAnswer ? PolicyError.InvalidAnswer : PolicyError.NotAwaitingAnswer;
        }

        var answered = asked with { Delivery = DecisionDelivery.Answered, Answer = reply.Answer };
        await ledger.ReplacedAsync(answered, cancellationToken);

        return answered;
    }

    public async ValueTask<bool> PassAsync(SessionId child, ItemId item, PassReason reason, CancellationToken cancellationToken) =>
        await ledger.PassedAsync(child, item, reason, cancellationToken);

    private static bool Asks(Option<JobId> asked, GovernedSession parent) => parent.Job.IsSome && asked == parent.Job;

    private bool AllowedTo(GovernedSession parent, Option<PermissionRequest> request) =>
        request.Match(found => parent.Policy.Decide(found, book.RulesOf(parent)).Answer == PolicyAnswer.Allow, () => false);
}
