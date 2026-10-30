using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Folders;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class HumanAnswers(GovernanceBook book, IAgents agents, IRepositoryRuleFiles repository, AnswerLedger ledger) : IPermissionAnswers
{
    public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
    {
        var governed = book.Of(session);

        if (governed.Decisions.LastOrDefault(decision => decision.Item == reply.Item) is not { Delivery: DecisionDelivery.LeftToHuman } asked)
        {
            return PolicyError.NotAwaitingAnswer;
        }

        if (governed.Job.IsSome && !Offered(reply, asked))
        {
            return PolicyError.ScopeNotOffered;
        }

        return await DecideAsync(governed, asked, reply, cancellationToken);
    }

    public static bool Everything(Remember remember) => remember is Remember.EverythingInThisJob or Remember.EverythingInThisContest;

    private async ValueTask<Result<HumanAnswer, PolicyError>> DecideAsync(GovernedSession governed, PolicyDecision asked, PermissionReply reply, CancellationToken cancellationToken)
    {
        var answer = PolicyAnswerTo(reply);
        var kept = governed.Job.Match(job => Rules(reply.Remember, asked, answer).Select(rule => (Job: job, Rule: rule)).ToList(), () => []);
        var raised = governed.Job.Match(job => Everything(reply.Remember) && ledger.Raise(job) ? new[] { job } : [], () => []);

        foreach (var (job, rule) in kept)
        {
            book.Remember(job, rule);
        }

        await ledger.JoinedAsync(governed.Session, cancellationToken);

        if ((await DeliverAsync(governed.Session, asked, reply, cancellationToken)).IsFailure)
        {
            Undo(kept, raised);

            return PolicyError.NotAwaitingAnswer;
        }

        foreach (var job in raised)
        {
            await ledger.RaisedAsync(job, cancellationToken);
        }

        return await RecordAsync(governed, asked, reply, kept.Select(found => found.Rule).FirstOrDefault().ToOption(), cancellationToken);
    }

    private ValueTask<Result<ItemId, AgentError>> DeliverAsync(SessionId session, PolicyDecision asked, PermissionReply reply, CancellationToken cancellationToken) =>
        asked.Folder.Match(
            folder => agents.ReturnAsync(session, FolderTool.Answered(reply.Item, folder, reply), cancellationToken),
            () => agents.RespondAsync(session, new PermissionDecision(reply.Item, reply.Answer) { Message = reply.Message }, cancellationToken));

    private void Undo(List<(JobId Job, PolicyRule Rule)> kept, JobId[] raised)
    {
        foreach (var (job, rule) in kept)
        {
            book.Forget(job, rule);
        }

        foreach (var job in raised)
        {
            ledger.Lower(job);
        }
    }

    private async ValueTask<Result<HumanAnswer, PolicyError>> RecordAsync(GovernedSession governed, PolicyDecision asked, PermissionReply reply, Option<PolicyRule> kept, CancellationToken cancellationToken)
    {
        var answer = PolicyAnswerTo(reply);
        var written = reply.Remember == Remember.InThisRepository
            ? Option<Result<PolicyRule, PolicyError>>.Some(await WriteAsync(governed.Job, asked.RepositoryRule.Map(rule => rule with { Answer = answer }), cancellationToken))
            : Option<Result<PolicyRule, PolicyError>>.None;

        return await ledger.RecordAsync(
            new HumanAnswer(governed.Session, governed.Job, reply.Item, asked.Kind, asked.Target, reply.Answer, reply.Message, kept, default)
            {
                RepositoryRule = written.Bind(result => result.Match(Option<PolicyRule>.Some, _ => Option<PolicyRule>.None)),
                RepositoryError = written.Bind(result => result.Match(_ => Option<PolicyError>.None, Option<PolicyError>.Some)),
                Everything = governed.Job.IsSome && Everything(reply.Remember),
            },
            cancellationToken);
    }

    private static PolicyAnswer PolicyAnswerTo(PermissionReply reply) =>
        reply.Answer == PermissionAnswer.Allow ? PolicyAnswer.Allow : PolicyAnswer.Deny;

    private static bool Offered(PermissionReply reply, PolicyDecision asked) => reply.Remember switch
    {
        Remember.ForThisJob or Remember.ForThisContest => asked.Scope.Match(scope => scope.Rule.IsSome, () => false),
        Remember.EverythingInThisJob or Remember.EverythingInThisContest => asked.Scope.Match(scope => scope.Raisable, () => false),
        Remember.ReadsInThisFolder => asked.Folder.IsSome,
        Remember.EditsInThisFolder => Folder(asked).IsSome,
        _ => asked.Folder.IsNone || reply.Answer == PermissionAnswer.Deny,
    };

    private static PolicyRule[] Rules(Remember remember, PolicyDecision asked, PolicyAnswer answer) => remember switch
    {
        Remember.ForThisJob or Remember.ForThisContest => asked.Scope.Bind(scope => scope.Rule).Match<PolicyRule[]>(
            rule => [Remembering.Contested(rule, remember == Remember.ForThisContest) with { Answer = answer }],
            () => []),
        Remember.InThisRepository => [Remembering.ForJob(asked.Kind, asked.Target, answer)],
        Remember.ReadsInThisFolder when answer == PolicyAnswer.Allow => Folder(asked).Match<PolicyRule[]>(folder => [Remembering.Granted(folder, FolderAccess.Read)], () => []),
        Remember.EditsInThisFolder when answer == PolicyAnswer.Allow => Folder(asked).Match<PolicyRule[]>(folder => [Remembering.Granted(folder, FolderAccess.Edit)], () => []),
        _ => [],
    };

    private static Option<string> Folder(PolicyDecision asked) =>
        asked.Folder.Match(request => Option<string>.Some(request.Folder), () => asked.Scope.Bind(scope => scope.Folder));

    private Task<Result<PolicyRule, PolicyError>> WriteAsync(Option<JobId> job, Option<PolicyRule> rule, CancellationToken cancellationToken) =>
        job.Match(
            found => rule.Match(
                written => repository.AddAsync(found, written, cancellationToken),
                () => Task.FromResult<Result<PolicyRule, PolicyError>>(PolicyError.NotARepositoryRule)),
            () => Task.FromResult<Result<PolicyRule, PolicyError>>(PolicyError.RepositoryUnwritable));
}
