using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Declarations;

internal sealed record CatchUpPlan(int Missed, DateTimeOffset Anchor, DateTimeOffset Next);

internal static class ScheduleClock
{
    public const int MostMissedCounted = 1000;

    private const int DaysAhead = 8;

    public static DateTimeOffset NextAfter(TriggerSchedule schedule, DateTimeOffset after, TimeZoneInfo zone)
    {
        if (schedule.Kind == TriggerKind.Interval)
        {
            return after + TimeSpan.FromMinutes((long)schedule.EveryMinutes);
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);

        return Enumerable.Range(0, DaysAhead)
            .Select(today.AddDays)
            .Where(date => schedule.Allows(date.DayOfWeek))
            .Select(date => Occurrence(date, schedule.At, zone))
            .First(occurrence => occurrence > after);
    }

    public static DateTimeOffset Occurrence(DateOnly date, TimeOnly at, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(at, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            return new DateTimeOffset(local, zone.GetUtcOffset(local.AddDays(-1)));
        }

        return zone.IsAmbiguousTime(local)
            ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max())
            : new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    public static CatchUpPlan Plan(TriggerSchedule schedule, DateTimeOffset anchor, DateTimeOffset now, TimeZoneInfo zone)
    {
        var due = NextAfter(schedule, anchor, zone);

        if (due > now)
        {
            return new CatchUpPlan(0, anchor, due);
        }

        var (missed, last) = Missed(schedule, anchor, now, zone);

        return new CatchUpPlan(missed, last, NextAfter(schedule, last, zone));
    }

    private static (int Missed, DateTimeOffset Last) Missed(TriggerSchedule schedule, DateTimeOffset anchor, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (schedule.Kind == TriggerKind.Interval)
        {
            var periods = (long)((now - anchor).TotalMinutes / schedule.EveryMinutes);

            return ((int)Math.Min(periods, MostMissedCounted), anchor + TimeSpan.FromMinutes(periods * schedule.EveryMinutes));
        }

        var count = 0;
        var last = Option<DateTimeOffset>.None;

        for (var next = NextAfter(schedule, anchor, zone); next <= now && count < MostMissedCounted; next = NextAfter(schedule, next, zone))
        {
            count++;
            last = next;
        }

        return (count, count < MostMissedCounted ? last.Match(found => found, () => anchor) : LastBefore(schedule, now, zone));
    }

    private static DateTimeOffset LastBefore(TriggerSchedule schedule, DateTimeOffset now, TimeZoneInfo zone)
    {
        var start = NextAfter(schedule, now.AddDays(-DaysAhead), zone);
        var last = start;

        for (var next = start; next <= now; next = NextAfter(schedule, next, zone))
        {
            last = next;
        }

        return last;
    }
}
