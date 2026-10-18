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

    public Func<DelegationRecord, Task> BeforeWrite { get; set; } = _ => Task.CompletedTask;

    public async Task RecordAsync(DelegationRecord record, CancellationToken cancellationToken)
    {
        await BeforeWrite(record);

        if (Fails(record))
        {
            throw new IOException("The delegation database is locked");
        }

        recorded.Enqueue(record);
    }

    public Task<IReadOnlyList<DelegationRecord>> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);
}
