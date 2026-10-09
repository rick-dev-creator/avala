using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Level = Avala.Jobs.Contracts.Autonomy;

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

    public Option<SessionAutonomy> Autonomy { get; init; }

    public IReadOnlyList<FormDecision> Forms { get; init; } = [];

    public GovernedSession OpenedIn(string workingDirectory, PermissionPolicy policy, SessionPolicy report) =>
        this with { WorkingDirectory = workingDirectory, Policy = policy, Report = report };

    public GovernedSession WorkingOn(JobId job) => WorkingOn(job, Option<Level>.None);

    public GovernedSession WorkingOn(JobId job, Option<Level> requested)
    {
        var policy = Policy.Capped(requested);
        var refused = requested.Match(level => level > Policy.Declared, () => false);

        return this with
        {
            Job = job,
            Policy = policy,
            Autonomy = new SessionAutonomy(Session, job, Policy.Declared, requested, policy.Autonomy, refused),
        };
    }

    public GovernedSession Decided(PolicyDecision decision) => this with { Decisions = [.. Decisions, decision] };

    public GovernedSession Asked(FormDecision decision) => this with { Forms = [.. Forms, decision] };
}
