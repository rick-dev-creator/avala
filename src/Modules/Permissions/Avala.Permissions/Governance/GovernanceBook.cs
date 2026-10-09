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
    private ImmutableDictionary<SessionId, ImmutableList<PolicyRule>> rules = ImmutableDictionary<SessionId, ImmutableList<PolicyRule>>.Empty;
    private ImmutableList<HumanAnswer> answers = [];

    public GovernedSession Of(SessionId session) => Volatile.Read(ref sessions).GetValueOrDefault(session) ?? new GovernedSession(session);

    public void Keep(GovernedSession session) =>
        ImmutableInterlocked.AddOrUpdate(ref sessions, session.Session, session, (_, _) => session);

    public void Remember(SessionId session, PolicyRule rule) =>
        ImmutableInterlocked.AddOrUpdate(ref rules, session, [rule], (_, kept) => kept.Add(rule));

    public void Forget(SessionId session, PolicyRule rule) =>
        ImmutableInterlocked.AddOrUpdate(ref rules, session, [], (_, kept) => kept.Remove(rule));

    public void Record(HumanAnswer answer) => ImmutableInterlocked.Update(ref answers, recorded => recorded.Add(answer));

    public Option<SessionPolicy> PolicyOf(SessionId session) => Of(session).Report;

    public Option<SessionAutonomy> AutonomyOf(SessionId session) => Of(session).Autonomy;

    public IReadOnlyList<PolicyRule> SessionRulesOf(SessionId session) => Volatile.Read(ref rules).GetValueOrDefault(session) ?? [];

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => Of(session).Decisions;

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) =>
        [.. OfEverySession(job).SelectMany(session => session.Decisions).OrderBy(decision => decision.At)];

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => Of(session).Forms;

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) =>
        [.. OfEverySession(job).SelectMany(session => session.Forms).OrderBy(decision => decision.At)];

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) =>
        [.. Volatile.Read(ref answers).Where(answer => answer.Job == Option<JobId>.Some(job))];

    private IEnumerable<GovernedSession> OfEverySession(JobId job) =>
        Volatile.Read(ref sessions).Values.Where(session => session.Job == Option<JobId>.Some(job));
}
