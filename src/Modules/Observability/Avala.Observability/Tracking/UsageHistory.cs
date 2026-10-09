using Avala.Observability.Contracts;
using Avala.Observability.Usage;

namespace Avala.Observability.Tracking;

internal sealed class UsageHistory(IUsageStore store) : IUsageHistory
{
    public async ValueTask<UsagePeriod> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Period(from, to, await store.WithinAsync(from, to, cancellationToken));

    public async ValueTask<IReadOnlyList<UsagePeriod>> DailyAsync(DateOnly first, DateOnly last, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var days = Enumerable.Range(0, Math.Max(0, last.DayNumber - first.DayNumber + 1)).Select(first.AddDays).ToList();

        if (days.Count == 0)
        {
            return [];
        }

        var stored = await store.WithinAsync(Midnight(days[0], zone), Midnight(last.AddDays(1), zone), cancellationToken);

        return
        [
            .. days.Select(day =>
            {
                var (from, to) = (Midnight(day, zone), Midnight(day.AddDays(1), zone));

                return Period(from, to, stored with { Facts = [.. stored.Facts.Where(fact => fact.At >= from && fact.At < to)] });
            }),
        ];
    }

    private static UsagePeriod Period(DateTimeOffset from, DateTimeOffset to, StoredUsage stored)
    {
        var active = stored.Facts.Select(fact => fact.Session).ToHashSet();
        var sessions = stored.Sessions.Where(session => active.Contains(session.Session)).Recording(stored.Facts);

        return new UsagePeriod(from, to, sessions.Summary, sessions.ByProvider(), sessions.ByConnection());
    }

    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
