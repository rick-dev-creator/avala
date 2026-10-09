using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Permissions.Answering;

internal sealed class HumanAnswers(GovernanceBook book, IAgents agents, IEventBus bus, TimeProvider clock) : IPermissionAnswers
{
    public const string DontAskAgain = "don't ask again this session";

    public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
    {
        var governed = book.Of(session);

        if (governed.Decisions.LastOrDefault(decision => decision.Item == reply.Item) is not { Delivery: DecisionDelivery.LeftToHuman } asked)
        {
            return PolicyError.NotAwaitingAnswer;
        }

        var answer = reply.Answer == PermissionAnswer.Allow ? PolicyAnswer.Allow : PolicyAnswer.Deny;
        var rule = new PolicyRule(RuleOrigin.Session, DontAskAgain, asked.Kind, asked.Target, RuleScope.Anywhere, answer);

        if (reply.DontAskAgain)
        {
            book.Remember(session, rule);
        }

        if ((await agents.RespondAsync(session, new PermissionDecision(reply.Item, reply.Answer) { Message = reply.Message }, cancellationToken)).IsFailure)
        {
            if (reply.DontAskAgain)
            {
                book.Forget(session, rule);
            }

            return PolicyError.NotAwaitingAnswer;
        }

        var human = new HumanAnswer(
            session,
            governed.Job,
            reply.Item,
            asked.Kind,
            asked.Target,
            reply.Answer,
            reply.Message,
            reply.DontAskAgain ? rule : Option<PolicyRule>.None,
            clock.GetUtcNow());
        await book.RecordAsync(human, cancellationToken);
        await bus.PublishAsync(new PermissionAnswered(human), cancellationToken);

        return human;
    }
}
