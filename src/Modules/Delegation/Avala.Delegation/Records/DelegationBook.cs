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

    public async Task<IReadOnlyList<DelegationRecord>> ClaimAsync(IReadOnlyList<DelegationRecord> reported, CallAnswer answer, CancellationToken cancellationToken)
    {
        var claimed = new List<DelegationRecord>();

        foreach (var record in reported)
        {
            var claim = record with { Answered = answer };

            if (!Swap(record, current => current.Answered.IsNone, claim))
            {
                continue;
            }

            claimed.Add(claim);

            try
            {
                await store.RecordAsync(claim, cancellationToken);
            }
            catch
            {
                foreach (var undone in claimed)
                {
                    _ = Swap(undone, current => current.Answered == undone.Answered, undone with { Answered = Option<CallAnswer>.None });
                }

                throw;
            }
        }

        return claimed;
    }

    public async Task ReleaseAsync(IReadOnlyList<DelegationRecord> claimed, CancellationToken cancellationToken)
    {
        foreach (var claim in claimed)
        {
            var released = claim with { Answered = Option<CallAnswer>.None };

            if (Swap(claim, current => current.Answered == claim.Answered, released))
            {
                await store.RecordAsync(released, cancellationToken);
            }
        }
    }

    public bool Holds(DelegationRecord claim) =>
        Volatile.Read(ref records).Exists(current => Same(current, claim) && current.Answered == claim.Answered);

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

    private bool Swap(DelegationRecord record, Func<DelegationRecord, bool> expected, DelegationRecord replacement)
    {
        var swapped = false;
        ImmutableInterlocked.Update(ref records, kept =>
        {
            var at = kept.FindIndex(existing => Same(existing, record));
            swapped = at >= 0 && expected(kept[at]);

            return swapped ? kept.SetItem(at, replacement) : kept;
        });

        return swapped;
    }

    private static ImmutableList<DelegationRecord> Kept(ImmutableList<DelegationRecord> kept, DelegationRecord record) =>
        kept.FindIndex(existing => Same(existing, record)) is var at and >= 0 ? kept.SetItem(at, record) : kept.Add(record);

    private static bool Same(DelegationRecord one, DelegationRecord other) => one.Session == other.Session && one.Item == other.Item;
}
