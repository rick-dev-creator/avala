using System.Collections.Immutable;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal sealed class DelegationBook(IDelegationStore store) : IDelegations, IStartupTask
{
    private ImmutableList<DelegationRecord> records = [];
    private IReadOnlyList<DelegationRecord>? earlier;
    private Task? restoring;

    public async Task KeepAsync(DelegationRecord record, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref records, kept => Kept(kept, record));
        await store.RecordAsync(record, cancellationToken);
    }

    public Task RunAsync(CancellationToken cancellationToken) => RestoreAsync(cancellationToken);

    public Task RestoreAsync(CancellationToken cancellationToken) => restoring ??= LoadAsync(cancellationToken);

    public Option<IReadOnlyList<DelegationRecord>> Earlier => Volatile.Read(ref earlier).ToOption();

    public IReadOnlyList<DelegationRecord> All() => Volatile.Read(ref records);

    public IReadOnlyList<DelegationRecord> OfParent(JobId parent) =>
        [.. Volatile.Read(ref records).Where(record => record.Parent == Option<JobId>.Some(parent))];

    public Option<DelegationRecord> OfChild(JobId child) =>
        Volatile.Read(ref records).FirstOrDefault(record => record.Child == Option<JobId>.Some(child)).ToOption();

    public IReadOnlyList<DelegationRecord> OwedTo(JobId parent) => [.. OfParent(parent).Where(Owed)];

    public static bool Owed(DelegationRecord record) => record.Child.IsSome && record.Answered.IsNone;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var restored = await store.EarlierRunsAsync(cancellationToken);
        ImmutableInterlocked.Update(ref records, live => [.. restored.Where(record => !live.Exists(other => Same(other, record))), .. live]);
        Volatile.Write(ref earlier, restored);
    }

    private static ImmutableList<DelegationRecord> Kept(ImmutableList<DelegationRecord> kept, DelegationRecord record) =>
        kept.FindIndex(existing => Same(existing, record)) is var at and >= 0 ? kept.SetItem(at, record) : kept.Add(record);

    private static bool Same(DelegationRecord one, DelegationRecord other) => one.Session == other.Session && one.Item == other.Item;
}
