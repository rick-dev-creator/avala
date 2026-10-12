using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class HumanAnswers(GovernanceBook book, IAgents agents, IRepositoryRuleFiles repository, AnswerLedger ledger) : IPermissionAnswers
{
    public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
    {
        var governed = book.Of(session);

        if (governed.Decisions.LastOrDefault(decision => decision.Item == reply.Item) is not { Delivery: DecisionDelivery.LeftToHuman or DecisionDelivery.LeftToParent } asked)
        {
            return PolicyError.NotAwaitingAnswer;
        }

        if (governed.Job.IsSome && !Offered(reply.Remember, asked))
        {
            return PolicyError.ScopeNotOffered;
        }

        var answer = reply.Answer == PermissionAnswer.Allow ? PolicyAnswer.Allow : PolicyAnswer.Deny;
        var kept = governed.Job.Match(job => Rules(reply.Remember, asked, answer).Select(rule => (Job: job, Rule: rule)).ToList(), () => []);
        var raised = governed.Job.Match(job => Everything(reply.Remember) && ledger.Raise(job) ? new[] { job } : [], () => []);

        foreach (var (job, rule) in kept)
        {
            book.Remember(job, rule);
        }

        if ((await agents.RespondAsync(session, new PermissionDecision(reply.Item, reply.Answer) { Message = reply.Message }, cancellationToken)).IsFailure)
        {
            foreach (var (job, rule) in kept)
            {
                book.Forget(job, rule);
            }

            foreach (var job in raised)
            {
                ledger.Lower(job);
            }

            return PolicyError.NotAwaitingAnswer;
        }

        foreach (var job in raised)
        {
            await ledger.RaisedAsync(job, cancellationToken);
        }

        var written = reply.Remember == Remember.InThisRepository
            ? Option<Result<PolicyRule, PolicyError>>.Some(await WriteAsync(governed.Job, asked.RepositoryRule.Map(rule => rule with { Answer = answer }), cancellationToken))
            : Option<Result<PolicyRule, PolicyError>>.None;

        return await ledger.RecordAsync(
            new HumanAnswer(session, governed.Job, reply.Item, asked.Kind, asked.Target, reply.Answer, reply.Message, kept.Select(found => found.Rule).FirstOrDefault().ToOption(), default)
            {
                RepositoryRule = written.Bind(result => result.Match(Option<PolicyRule>.Some, _ => Option<PolicyRule>.None)),
                RepositoryError = written.Bind(result => result.Match(_ => Option<PolicyError>.None, Option<PolicyError>.Some)),
                Everything = governed.Job.IsSome && Everything(reply.Remember),
            },
            cancellationToken);
    }

    public static bool Everything(Remember remember) => remember is Remember.EverythingInThisJob or Remember.EverythingInThisContest;

    private static bool Offered(Remember remember, PolicyDecision asked) => remember switch
    {
        Remember.ForThisJob or Remember.ForThisContest => asked.Scope.Match(scope => scope.Rule.IsSome, () => false),
        Remember.EverythingInThisJob or Remember.EverythingInThisContest => asked.Scope.Match(scope => scope.Raisable, () => false),
        _ => true,
    };

    private static PolicyRule[] Rules(Remember remember, PolicyDecision asked, PolicyAnswer answer) => remember switch
    {
        Remember.ForThisJob or Remember.ForThisContest => asked.Scope.Bind(scope => scope.Rule).Match<PolicyRule[]>(
            rule => [Remembering.Contested(rule, remember == Remember.ForThisContest) with { Answer = answer }],
            () => []),
        Remember.InThisRepository => [Remembering.ForJob(asked.Kind, asked.Target, answer)],
        _ => [],
    };

    private Task<Result<PolicyRule, PolicyError>> WriteAsync(Option<JobId> job, Option<PolicyRule> rule, CancellationToken cancellationToken) =>
        job.Match(
            found => rule.Match(
                written => repository.AddAsync(found, written, cancellationToken),
                () => Task.FromResult<Result<PolicyRule, PolicyError>>(PolicyError.NotARepositoryRule)),
            () => Task.FromResult<Result<PolicyRule, PolicyError>>(PolicyError.RepositoryUnwritable));
}
