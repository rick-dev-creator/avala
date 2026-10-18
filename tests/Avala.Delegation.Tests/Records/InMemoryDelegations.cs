using System.Collections.Concurrent;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;

namespace Avala.Delegation.Tests.Records;

internal sealed class InMemoryDelegations : IDelegationStore
{
    private readonly ConcurrentQueue<DelegationRecord> recorded = new();

    public IReadOnlyList<DelegationRecord> Recorded => [.. recorded];

    public IReadOnlyList<DelegationRecord> Earlier { get; set; } = [];

    public Func<DelegationRecord, bool> Fails { get; set; } = _ => false;

    public Task RecordAsync(DelegationRecord record, CancellationToken cancellationToken)
    {
        if (Fails(record))
        {
            throw new IOException("The delegation database is locked");
        }

        recorded.Enqueue(record);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DelegationRecord>> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);
}
