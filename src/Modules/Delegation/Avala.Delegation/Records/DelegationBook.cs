using System.Collections.Immutable;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal sealed class DelegationBook : IDelegations
{
    private ImmutableList<DelegationRecord> records = [];

    public void Keep(DelegationRecord record) =>
        ImmutableInterlocked.Update(
            ref records,
            kept => kept.FindIndex(earlier => earlier.Session == record.Session && earlier.Item == record.Item) is var at and >= 0
                ? kept.SetItem(at, record)
                : kept.Add(record));

    public IReadOnlyList<DelegationRecord> All() => Volatile.Read(ref records);

    public IReadOnlyList<DelegationRecord> OfParent(JobId parent) =>
        [.. Volatile.Read(ref records).Where(record => record.Parent == Option<JobId>.Some(parent))];

    public Option<DelegationRecord> OfChild(JobId child) =>
        Volatile.Read(ref records).FirstOrDefault(record => record.Child == Option<JobId>.Some(child)).ToOption();
}
