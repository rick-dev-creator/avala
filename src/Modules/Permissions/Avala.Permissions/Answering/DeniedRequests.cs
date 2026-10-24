using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class DeniedRequests(GovernanceBook book, AnswerLedger ledger) : IDeniedRequests
{
    public async ValueTask<Result<HumanAnswer, PolicyError>> AllowAsync(SessionId session, ItemId item, CancellationToken cancellationToken)
    {
        var governed = book.Of(session);
        var denied = governed.Decisions.LastOrDefault(decision => decision.Item == item) is { Delivery: DecisionDelivery.Answered, Answer: PolicyAnswer.Deny, Autonomy: Autonomy.Autonomous } found
            ? governed.Job.Map(job => (Job: job, Decision: found))
            : Option<(JobId Job, PolicyDecision Decision)>.None;

        return await denied.Match(
            known => known.Decision.Scope.Bind(scope => scope.Rule.Match(Option<PolicyRule>.Some, () => scope.Folder.Map(folder => Remembering.Granted(folder, FolderAccess.Edit)))).Match(
                rule => AllowAsync(known.Job, known.Decision, rule with { Answer = PolicyAnswer.Allow }, cancellationToken),
                () => Task.FromResult(Result<HumanAnswer, PolicyError>.Failure(PolicyError.ScopeNotOffered))),
            () => Task.FromResult(Result<HumanAnswer, PolicyError>.Failure(PolicyError.NotAwaitingAnswer)));
    }

    private async Task<Result<HumanAnswer, PolicyError>> AllowAsync(JobId job, PolicyDecision denied, PolicyRule rule, CancellationToken cancellationToken)
    {
        book.Remember(job, rule);

        return await ledger.RecordAsync(
            new HumanAnswer(denied.Session, job, denied.Item, denied.Kind, denied.Target, PermissionAnswer.Allow, Option<string>.None, rule, default),
            cancellationToken);
    }
}
