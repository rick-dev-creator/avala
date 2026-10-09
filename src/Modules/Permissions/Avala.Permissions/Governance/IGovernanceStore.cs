using Avala.Permissions.Contracts;

namespace Avala.Permissions.Governance;

internal sealed record GovernanceHistory(
    IReadOnlyList<SessionPolicy> Policies,
    IReadOnlyList<SessionAutonomy> Autonomies,
    IReadOnlyList<PolicyDecision> Decisions,
    IReadOnlyList<FormDecision> Forms,
    IReadOnlyList<HumanAnswer> Answers)
{
    public static GovernanceHistory Empty { get; } = new([], [], [], [], []);
}

internal interface IGovernanceStore
{
    Task RecordAsync(SessionPolicy policy, CancellationToken cancellationToken);

    Task RecordAsync(SessionAutonomy autonomy, CancellationToken cancellationToken);

    Task RecordAsync(PolicyDecision decision, CancellationToken cancellationToken);

    Task RecordAsync(FormDecision decision, CancellationToken cancellationToken);

    Task RecordAsync(HumanAnswer answer, CancellationToken cancellationToken);

    Task<GovernanceHistory> EarlierRunsAsync(CancellationToken cancellationToken);
}
