using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal sealed class DelegationBook(IDelegationStore store) : IDelegations, IStartupTask
{
    private Ledger ledger = new([], []);
    private IReadOnlyList<DelegationRecord>? earlier;
    private Task? restoring;

    public async Task KeepAsync(DelegationRecord record, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref ledger, kept => kept with { Records = Kept(kept.Records, record) });
        await store.RecordAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<DelegationRecord>> ClaimAsync(IReadOnlyList<DelegationRecord> reported, CallAnswer answer, CancellationToken cancellationToken)
    {
        var claimed = new List<DelegationRecord>();

        foreach (var record in reported)
        {
            var claim = record with { Answered = answer };

            if (!Change(record, (current, claims) => current.Answered.IsNone && !claims.Contains(Key(record)), claim, claims => claims.Add(Key(record))))
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
                    _ = Change(undone, Claimed(undone), undone with { Answered = Option<CallAnswer>.None }, claims => claims.Remove(Key(undone)));
                }

                throw;
            }
        }

        return claimed;
    }

    public IReadOnlyList<DelegationRecord> Confirm(IReadOnlyList<DelegationRecord> claimed) =>
        [.. claimed.Where(claim => Change(claim, Claimed(claim), claim, claims => claims.Remove(Key(claim))))];

    public async Task ReleaseAsync(IReadOnlyList<DelegationRecord> claimed, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        foreach (var claim in claimed)
        {
            var released = claim with { Answered = Option<CallAnswer>.None };

            if (!Change(claim, Claimed(claim), released, claims => claims.Remove(Key(claim))))
            {
                continue;
            }

            try
            {
                await store.RecordAsync(released, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    public async Task<bool> AnswerAsync(DelegationRecord record, CallAnswer answer, CancellationToken cancellationToken)
    {
        var answered = record with { Answered = answer };

        if (!Change(record, (current, claims) => current.Answered.IsNone || claims.Contains(Key(record)), answered, claims => claims.Remove(Key(record))))
        {
            return false;
        }

        await store.RecordAsync(answered, cancellationToken);

        return true;
    }

    public bool Unanswered(DelegationRecord record)
    {
        var current = Volatile.Read(ref ledger);

        return current.Records.Exists(kept => Same(kept, record) && kept.Answered.IsNone) || current.Claims.Contains(Key(record));
    }

    public Task RunAsync(CancellationToken cancellationToken) => RestoreAsync(cancellationToken);

    public Task RestoreAsync(CancellationToken cancellationToken) => restoring ??= LoadAsync(cancellationToken);

    public Option<IReadOnlyList<DelegationRecord>> Earlier => Volatile.Read(ref earlier).ToOption();

    public IReadOnlyList<DelegationRecord> All() => Volatile.Read(ref ledger).Records;

    public IReadOnlyList<DelegationRecord> OfParent(JobId parent) =>
        [.. Volatile.Read(ref ledger).Records.Where(record => record.Parent == Option<JobId>.Some(parent))];

    public Option<DelegationRecord> OfChild(JobId child) =>
        Volatile.Read(ref ledger).Records.FirstOrDefault(record => record.Child == Option<JobId>.Some(child)).ToOption();

    public IReadOnlyList<DelegationRecord> OwedTo(JobId parent) => [.. OfParent(parent).Where(Owed)];

    public static bool Owed(DelegationRecord record) => record.Child.IsSome && record.Answered.IsNone;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var restored = await store.EarlierRunsAsync(cancellationToken);
        ImmutableInterlocked.Update(ref ledger, live => live with { Records = [.. restored.Where(record => !live.Records.Exists(other => Same(other, record))), .. live.Records] });
        Volatile.Write(ref earlier, restored);
    }

    private static Func<DelegationRecord, ImmutableHashSet<(SessionId, ItemId)>, bool> Claimed(DelegationRecord claim) =>
        (current, claims) => claims.Contains(Key(claim)) && current.Answered == claim.Answered;

    private bool Change(
        DelegationRecord record,
        Func<DelegationRecord, ImmutableHashSet<(SessionId, ItemId)>, bool> expected,
        DelegationRecord replacement,
        Func<ImmutableHashSet<(SessionId, ItemId)>, ImmutableHashSet<(SessionId, ItemId)>> claimed)
    {
        var changed = false;
        ImmutableInterlocked.Update(ref ledger, kept =>
        {
            var at = kept.Records.FindIndex(existing => Same(existing, record));
            changed = at >= 0 && expected(kept.Records[at], kept.Claims);

            return changed ? new Ledger(kept.Records.SetItem(at, replacement), claimed(kept.Claims)) : kept;
        });

        return changed;
    }

    private static (SessionId, ItemId) Key(DelegationRecord record) => (record.Session, record.Item);

    private static ImmutableList<DelegationRecord> Kept(ImmutableList<DelegationRecord> kept, DelegationRecord record) =>
        kept.FindIndex(existing => Same(existing, record)) is var at and >= 0 ? kept.SetItem(at, record) : kept.Add(record);

    private static bool Same(DelegationRecord one, DelegationRecord other) => one.Session == other.Session && one.Item == other.Item;

    private sealed record Ledger(ImmutableList<DelegationRecord> Records, ImmutableHashSet<(SessionId, ItemId)> Claims);
}
