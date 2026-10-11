using System.Collections.Immutable;
using Avala.Notifications.Notices;
using Avala.Sdk;

namespace Avala.Notifications.Pacing;

internal sealed record Pending(Notice Notice, DateTimeOffset Due, ImmutableHashSet<string> Handles)
{
    public static Pending Of(Notice notice, DateTimeOffset due) => new(notice, due, [notice.Handle]);
}

internal sealed record Outbox(ImmutableList<Pending> Entries)
{
    public static TimeSpan Coalescing { get; } = TimeSpan.FromSeconds(30);

    public static Outbox Empty { get; } = new([]);

    public Option<DateTimeOffset> NextDue => Entries.IsEmpty ? Option<DateTimeOffset>.None : Entries.Min(entry => entry.Due);

    public Outbox Offer(Notice notice, DateTimeOffset now)
    {
        if (Entries.Find(entry => entry.Notice.Subject == notice.Subject) is { } same)
        {
            return new(Entries.Replace(same, same with { Notice = same.Notice.Joined(notice), Handles = same.Handles.Add(notice.Handle) }));
        }

        var open = Entries.Where(entry => entry.Due > now).Select(entry => entry.Due).DefaultIfEmpty(now + Coalescing).Min();

        return new(Entries.Add(Pending.Of(notice, open < now + Coalescing ? open : now + Coalescing)));
    }

    public (Outbox Left, IReadOnlyList<Pending> Withdrawn) Resolve(string handle)
    {
        var touched = Entries.Where(entry => entry.Handles.Contains(handle)).ToList();
        var withdrawn = touched.Where(entry => entry.Handles.Count == 1).ToList();
        var left = touched.Aggregate(Entries, (entries, entry) => entry.Handles.Count == 1 ? entries.Remove(entry) : entries.Replace(entry, entry with { Handles = entry.Handles.Remove(handle) }));

        return (new Outbox(left), withdrawn);
    }

    public (Outbox Left, IReadOnlyList<Pending> Ripe) Ripen(DateTimeOffset now)
    {
        var ripe = Entries.Where(entry => entry.Due <= now).ToList();

        return (new Outbox(Entries.RemoveRange(ripe)), ripe);
    }

    public Outbox Later(IEnumerable<Pending> entries) => new(Entries.AddRange(entries));
}
