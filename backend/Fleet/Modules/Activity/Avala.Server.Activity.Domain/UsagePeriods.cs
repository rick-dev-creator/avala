namespace Avala.Server.Activity.Domain;

internal enum UsageStep
{
    Day,
    Week,
    Month,
}

internal sealed record UsagePeriod(DateOnly Start, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

internal static class UsagePeriods
{
    public const int MaximumDays = 400;

    public static Admission<IReadOnlyList<UsagePeriod>> Of(DateOnly from, DateOnly to, TimeZoneInfo zone, UsageStep step)
    {
        if (from >= to)
        {
            return Admission.Refuse<IReadOnlyList<UsagePeriod>>("the range must end after it starts");
        }

        if (to.DayNumber - from.DayNumber > MaximumDays)
        {
            return Admission.Refuse<IReadOnlyList<UsagePeriod>>($"the range is longer than {MaximumDays} days");
        }

        if (step == UsageStep.Week && (from.DayOfWeek != DayOfWeek.Monday || to.DayOfWeek != DayOfWeek.Monday))
        {
            return Admission.Refuse<IReadOnlyList<UsagePeriod>>("a range by week must start and end on a Monday");
        }

        if (step == UsageStep.Month && (from.Day != 1 || to.Day != 1))
        {
            return Admission.Refuse<IReadOnlyList<UsagePeriod>>("a range by month must start and end on the first of a month");
        }

        var starts = Starts(from, to, step).ToList();

        return Admission.Admit<IReadOnlyList<UsagePeriod>>(
        [
            .. starts.Zip(starts.Skip(1), (start, end) => new UsagePeriod(start, Instant(start, zone), Instant(end, zone))),
        ]);
    }

    public static DateTimeOffset Instant(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        while (zone.IsInvalidTime(local))
        {
            local = local.Add(UsageQuarter.Length);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static IEnumerable<DateOnly> Starts(DateOnly from, DateOnly to, UsageStep step)
    {
        for (var start = from; start < to; start = Next(start, step))
        {
            yield return start;
        }

        yield return to;
    }

    private static DateOnly Next(DateOnly start, UsageStep step) => step switch
    {
        UsageStep.Week => start.AddDays(7),
        UsageStep.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };
}
