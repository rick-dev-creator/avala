using System.Collections.Immutable;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal sealed class DelegationBook(IDelegationStore store) : IDelegations, IStartupTask
{
    private ImmutableList<DelegationRecord> records = [];

    public async Task KeepAsync(DelegationRecord record, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref records, kept => Kept(kept, record));
        await store.RecordAsync(record, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var earlier = await store.EarlierRunsAsync(cancellationToken);
        ImmutableInterlocked.Update(ref records, live => [.. earlier.Where(record => !live.Exists(other => Same(other, record))), .. live]);
    }

    public IReadOnlyList<DelegationRecord> All() => Volatile.Read(ref records);

    public IReadOnlyList<DelegationRecord> OfParent(JobId parent) =>
        [.. Volatile.Read(ref records).Where(record => record.Parent == Option<JobId>.Some(parent))];

    public Option<DelegationRecord> OfChild(JobId child) =>
        Volatile.Read(ref records).FirstOrDefault(record => record.Child == Option<JobId>.Some(child)).ToOption();

    private static ImmutableList<DelegationRecord> Kept(ImmutableList<DelegationRecord> kept, DelegationRecord record) =>
        kept.FindIndex(earlier => Same(earlier, record)) is var at and >= 0 ? kept.SetItem(at, record) : kept.Add(record);

    private static bool Same(DelegationRecord one, DelegationRecord other) => one.Session == other.Session && one.Item == other.Item;
}
