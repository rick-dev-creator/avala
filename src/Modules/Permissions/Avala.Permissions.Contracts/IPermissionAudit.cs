using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Contracts;

public interface IPermissionAudit
{
    Option<SessionPolicy> PolicyOf(SessionId session);

    IReadOnlyList<PolicyDecision> OfSession(SessionId session);

    IReadOnlyList<PolicyDecision> OfJob(JobId job);
}
