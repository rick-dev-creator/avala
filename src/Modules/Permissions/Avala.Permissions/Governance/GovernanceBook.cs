using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Governance;

internal sealed class GovernanceBook : IPermissionAudit
{
    private ImmutableDictionary<SessionId, GovernedSession> sessions = ImmutableDictionary<SessionId, GovernedSession>.Empty;

    public GovernedSession Of(SessionId session) => Volatile.Read(ref sessions).GetValueOrDefault(session) ?? new GovernedSession(session);

    public void Keep(GovernedSession session) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, session.Session, session, (_, _) => session);

    public Option<SessionPolicy> PolicyOf(SessionId session) => Of(session).Report;

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => Of(session).Decisions;

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) =>
    [
        .. Volatile.Read(ref sessions).Values
            .Where(session => session.Job == Option<JobId>.Some(job))
            .SelectMany(session => session.Decisions)
            .OrderBy(decision => decision.At),
    ];
}
