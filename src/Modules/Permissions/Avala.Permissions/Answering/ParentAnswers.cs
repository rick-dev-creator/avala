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
    public async ValueTask<Result<FormDecision, PolicyError>> AnswerFormAsync(SessionId child, ParentFormReply reply, CancellationToken cancellationToken)
    {
        var item = reply.Answer.Item;

        if (!book.Of(child).FormWaitingParent(item).ToResult(PolicyError.NotAwaitingAnswer).TryGetValue(out var asked, out _) || !Asks(asked.Parent, book.Of(reply.Parent)))
        {
            return PolicyError.NotAwaitingAnswer;
        }

        if (!book.Claim(child, item))
        {
            return PolicyError.NotAwaitingAnswer;
        }

        var answer = reply.Answer with { AnsweredBy = MessageAuthor.Avala };
        var delivered = await agents.AnswerAsync(child, answer, cancellationToken);

        if (!delivered.TryGetValue(out _, out var error))
        {
            book.Reopen(child, item);

            return error == AgentError.InvalidAnswer ? PolicyError.InvalidAnswer : PolicyError.NotAwaitingAnswer;
        }

        var answered = asked with { Delivery = DecisionDelivery.Answered, Answer = answer };
        await ledger.ReplacedAsync(answered, cancellationToken);

        return answered;
    }

    public bool Waits(SessionId child, ItemId item) => book.Of(child).Escalated.Contains(item);

    private static bool Asks(Option<JobId> asked, GovernedSession parent) => parent.Job.IsSome && asked == parent.Job;
}
