using Avala.Delegation.Contracts;

namespace Avala.Delegation.Records;

internal interface IDelegationStore
{
    Task RecordAsync(DelegationRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<DelegationRecord>> EarlierRunsAsync(CancellationToken cancellationToken);
}
