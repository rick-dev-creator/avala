using System.Collections.Concurrent;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Governance;

internal sealed class GovernanceBook : IPermissionAudit
{
    private readonly ConcurrentDictionary<SessionId, GovernedSession> sessions = new();

    public GovernedSession Of(SessionId session) => sessions.GetValueOrDefault(session) ?? new GovernedSession(session);

    public void Keep(GovernedSession session) => sessions[session.Session] = session;

    public Option<SessionPolicy> PolicyOf(SessionId session) => Of(session).Report;

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => Of(session).Decisions;

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) =>
    [
        .. sessions.Values
            .Where(session => session.Job == Option<JobId>.Some(job))
            .SelectMany(session => session.Decisions)
            .OrderBy(decision => decision.At),
    ];
}
