using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;

namespace Avala.Permissions.Tests.Governance;

internal sealed class InMemoryGovernance : IGovernanceStore
{
    public GovernanceHistory Earlier { get; set; } = GovernanceHistory.Empty;

    public List<object> Recorded { get; } = [];

    public Task RecordAsync(SessionPolicy policy, CancellationToken cancellationToken) => KeepAsync(policy);

    public Task RecordAsync(SessionAutonomy autonomy, CancellationToken cancellationToken) => KeepAsync(autonomy);

    public Task RecordAsync(PolicyDecision decision, CancellationToken cancellationToken) => KeepAsync(decision);

    public Task RecordAsync(FormDecision decision, CancellationToken cancellationToken) => KeepAsync(decision);

    public Task RecordAsync(HumanAnswer answer, CancellationToken cancellationToken) => KeepAsync(answer);

    public Task RecordAsync(EndedJob ended, CancellationToken cancellationToken) => KeepAsync(ended);

    public Task<GovernanceHistory> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);

    private Task KeepAsync(object fact)
    {
        Recorded.Add(fact);

        return Task.CompletedTask;
    }
}
