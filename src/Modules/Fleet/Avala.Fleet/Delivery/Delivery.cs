namespace Avala.Fleet.Delivery;

internal static class Backoff
{
    private static readonly TimeSpan[] Waits =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    public static TimeSpan After(int failures) => Waits[Math.Clamp(failures, 1, Waits.Length) - 1];
}

internal static class DeferredExpiry
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromDays(30);

    public static bool Expired(DateTimeOffset firstDeferred, DateTimeOffset now) => now - firstDeferred > Lifetime;
}

internal static class Stamps
{
    public static DateTimeOffset Next(DateTimeOffset now, DateTimeOffset last)
    {
        var instant = now.ToUniversalTime();

        return instant > last ? instant : last;
    }
}

internal sealed record ReceiptEntry(string Id, string Reason);

internal sealed record Receipt(IReadOnlyList<Guid> Accepted, IReadOnlyList<Guid> Duplicates, IReadOnlyList<ReceiptEntry> Deferred, IReadOnlyList<ReceiptEntry> Rejected);

internal sealed record PlannedFact(Guid Id, string Reason);

internal sealed record ReceiptPlan(IReadOnlyList<Guid> Delete, IReadOnlyList<PlannedFact> Rejected, IReadOnlyList<PlannedFact> Deferred)
{
    public const string Unanswered = "the server did not answer for this event";

    public static ReceiptPlan Of(IReadOnlyList<Guid> sent, Receipt receipt)
    {
        var known = sent.ToHashSet();
        var rejected = Resolved(sent, known, receipt.Rejected);
        var deferred = Resolved(sent, known, receipt.Deferred);
        var answered = receipt.Accepted.Concat(receipt.Duplicates).Where(known.Contains).ToHashSet();
        answered.UnionWith(rejected.Select(fact => fact.Id));
        var kept = deferred.Select(fact => fact.Id).ToHashSet();

        return new ReceiptPlan(
            [.. sent.Where(answered.Contains)],
            rejected,
            [.. deferred, .. sent.Where(id => !answered.Contains(id) && !kept.Contains(id)).Select(id => new PlannedFact(id, Unanswered))]);
    }

    private static IReadOnlyList<PlannedFact> Resolved(IReadOnlyList<Guid> sent, HashSet<Guid> known, IReadOnlyList<ReceiptEntry> entries) =>
        [
            .. entries
                .Select(entry => (Id: IdOf(sent, entry.Id), entry.Reason))
                .Where(entry => entry.Id is { } id && known.Contains(id))
                .Select(entry => new PlannedFact(entry.Id!.Value, entry.Reason))
                .DistinctBy(fact => fact.Id),
        ];

    private static Guid? IdOf(IReadOnlyList<Guid> sent, string key) =>
        Guid.TryParse(key, out var id) ? id
        : key.StartsWith('#') && int.TryParse(key.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sent.Count ? sent[index]
        : null;
}
