using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Permissions.Governance;

internal sealed record EndedJob(JobId Job, JobStatus Status);

internal sealed record GovernanceHistory(
    IReadOnlyList<SessionPolicy> Policies,
    IReadOnlyList<SessionAutonomy> Autonomies,
    IReadOnlyList<PolicyDecision> Decisions,
    IReadOnlyList<FormDecision> Forms,
    IReadOnlyList<HumanAnswer> Answers)
{
    public static GovernanceHistory Empty { get; } = new([], [], [], [], []);

    public IReadOnlySet<JobId> Ended { get; init; } = new HashSet<JobId>();
}

internal interface IGovernanceStore
{
    Task RecordAsync(SessionPolicy policy, CancellationToken cancellationToken);

    Task RecordAsync(SessionAutonomy autonomy, CancellationToken cancellationToken);

    Task RecordAsync(PolicyDecision decision, CancellationToken cancellationToken);

    Task RecordAsync(FormDecision decision, CancellationToken cancellationToken);

    Task RecordAsync(HumanAnswer answer, CancellationToken cancellationToken);

    Task RecordAsync(EndedJob ended, CancellationToken cancellationToken);

    Task<GovernanceHistory> EarlierRunsAsync(CancellationToken cancellationToken);
}
