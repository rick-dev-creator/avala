using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record GovernedSession(
    SessionId Session,
    Option<string> WorkingDirectory,
    PermissionPolicy Policy,
    Option<SessionPolicy> Report,
    Option<JobId> Job,
    IReadOnlyList<PolicyDecision> Decisions)
{
    public GovernedSession(SessionId session)
        : this(session, Option<string>.None, PermissionPolicy.BuiltIn, Option<SessionPolicy>.None, Option<JobId>.None, [])
    {
    }

    public GovernedSession OpenedIn(string workingDirectory, PermissionPolicy policy, SessionPolicy report) =>
        this with { WorkingDirectory = workingDirectory, Policy = policy, Report = report };

    public GovernedSession WorkingOn(JobId job) => this with { Job = job };

    public GovernedSession Decided(PolicyDecision decision) => this with { Decisions = [.. Decisions, decision] };
}
