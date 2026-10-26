using System.Globalization;
using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.FleetActivity;

internal enum FleetRangeChoice
{
    LastSevenDays,
    LastThirtyDays,
    ThisMonth,
    ThreeMonths,
    SixMonths,
    TwelveMonths,
    Custom,
}

internal enum FleetRangeRefusal
{
    EndBeforeStart,
    LongerThanAYear,
    StartAfterToday,
}

internal sealed record FleetRange(FleetRangeChoice Choice, DateOnly From, DateOnly To, FleetStep Step)
{
    public string Caption => string.Create(CultureInfo.CurrentCulture, $"{FleetFigures.Day(From)} – {FleetFigures.Day(To.AddDays(-1))}");
}

internal static class FleetRanges
{
    public const int DailyDays = 62;

    public const int LongestDays = 366;

    public static FleetRange Of(FleetRangeChoice choice, DateOnly today)
    {
        var tomorrow = today.AddDays(1);
        var monday = Monday(today);
        var month = new DateOnly(today.Year, today.Month, 1);

        return choice switch
        {
            FleetRangeChoice.LastThirtyDays => new FleetRange(choice, today.AddDays(-29), tomorrow, FleetStep.Day),
            FleetRangeChoice.ThisMonth => new FleetRange(choice, month, tomorrow, FleetStep.Day),
            FleetRangeChoice.ThreeMonths => new FleetRange(choice, monday.AddDays(-12 * 7), monday.AddDays(7), FleetStep.Week),
            FleetRangeChoice.SixMonths => new FleetRange(choice, monday.AddDays(-25 * 7), monday.AddDays(7), FleetStep.Week),
            FleetRangeChoice.TwelveMonths => new FleetRange(choice, month.AddMonths(-11), month.AddMonths(1), FleetStep.Month),
            _ => new FleetRange(FleetRangeChoice.LastSevenDays, today.AddDays(-6), tomorrow, FleetStep.Day),
        };
    }

    public static FleetRange Before(FleetRange range)
    {
        var from = range.Step == FleetStep.Month ? range.From.AddMonths(-Months(range)) : range.From.AddDays(-Days(range));

        return range with { From = from, To = range.From };
    }

    public static string Previous(FleetRange range) => range.Step switch
    {
        FleetStep.Month => Previous(Months(range), "month"),
        FleetStep.Week => Previous(Days(range) / 7, "week"),
        _ => Previous(Days(range), "day"),
    };

    public static string Said(FleetRangeRefusal refusal) => refusal switch
    {
        FleetRangeRefusal.EndBeforeStart => "The end comes before the start",
        FleetRangeRefusal.StartAfterToday => "The start is after today",
        _ => "Choose at most a year",
    };

    public static Result<FleetRange, FleetRangeRefusal> Custom(DateOnly from, DateOnly to, DateOnly today)
    {
        var last = to > today ? today : to;

        if (from > today)
        {
            return FleetRangeRefusal.StartAfterToday;
        }

        if (last < from)
        {
            return FleetRangeRefusal.EndBeforeStart;
        }

        var days = last.DayNumber - from.DayNumber + 1;

        return days switch
        {
            > LongestDays => FleetRangeRefusal.LongerThanAYear,
            > DailyDays => new FleetRange(FleetRangeChoice.Custom, Monday(from), Monday(last).AddDays(7), FleetStep.Week),
            _ => new FleetRange(FleetRangeChoice.Custom, from, last.AddDays(1), FleetStep.Day),
        };
    }

    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static int Days(FleetRange range) => range.To.DayNumber - range.From.DayNumber;

    private static int Months(FleetRange range) => ((range.To.Year - range.From.Year) * 12) + range.To.Month - range.From.Month;

    private static string Previous(int count, string unit) =>
        count == 1 ? $"the previous {unit}" : string.Create(CultureInfo.CurrentCulture, $"the previous {count} {unit}s");
}
