using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Contracts;

public interface IPermissionAudit
{
    Option<SessionPolicy> PolicyOf(SessionId session);

    Option<SessionAutonomy> AutonomyOf(SessionId session);

    IReadOnlyList<PolicyRule> SessionRulesOf(SessionId session);

    IReadOnlyList<PolicyDecision> OfSession(SessionId session);

    IReadOnlyList<PolicyDecision> OfJob(JobId job);

    IReadOnlyList<FormDecision> FormsOfSession(SessionId session);

    IReadOnlyList<FormDecision> FormsOfJob(JobId job);

    IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job);
}

public interface IPermissionAnswers
{
    ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken);
}
