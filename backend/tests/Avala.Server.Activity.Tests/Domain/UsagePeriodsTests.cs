using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Domain;

public sealed class UsagePeriodsTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private static readonly TimeZoneInfo Santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    [Fact]
    public void DaysTileTheRangeAcrossASpringForward()
    {
        var days = UsagePeriods.Of(new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 31), Madrid, UsageStep.Day).Value;

        Assert.Equal(
            [Utc(2026, 3, 27, 23), Utc(2026, 3, 28, 23), Utc(2026, 3, 29, 22), Utc(2026, 3, 30, 22)],
            [.. days.Select(day => day.StartsAt), days[^1].EndsAt]);
    }

    [Fact]
    public void DaysTileTheRangeAcrossAFallBack()
    {
        var days = UsagePeriods.Of(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26), Madrid, UsageStep.Day).Value;

        Assert.Equal(TimeSpan.FromHours(25), Assert.Single(days).EndsAt - days[0].StartsAt);
    }

    [Fact]
    public void AMidnightThatDoesNotExistStartsAtTheFirstValidInstant() =>
        Assert.Equal(Utc(2026, 9, 6, 4), UsagePeriods.Instant(new DateOnly(2026, 9, 6), Santiago));

    [Fact]
    public void WeeksStartOnMonday()
    {
        var weeks = UsagePeriods.Of(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 26), TimeZoneInfo.Utc, UsageStep.Week).Value;

        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19)], weeks.Select(week => week.Start));
    }

    [Fact]
    public void MonthsStartOnTheFirst()
    {
        var months = UsagePeriods.Of(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1), TimeZoneInfo.Utc, UsageStep.Month).Value;

        Assert.Equal((3, Utc(2026, 2, 1, 0), Utc(2026, 4, 1, 0)), (months.Count, months[1].StartsAt, months[^1].EndsAt));
    }

    [Theory]
    [InlineData("2026-10-26", "2026-10-26", "Day", "the range must end after it starts")]
    [InlineData("2025-01-01", "2026-03-01", "Day", "the range is longer than 400 days")]
    [InlineData("2026-10-06", "2026-10-26", "Week", "a range by week must start and end on a Monday")]
    [InlineData("2026-01-02", "2026-04-01", "Month", "a range by month must start and end on the first of a month")]
    public void ARangeThatCannotBeCutIsRefused(string from, string to, string step, string reason) =>
        Assert.Equal(
            reason,
            UsagePeriods.Of(
                DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
                TimeZoneInfo.Utc,
                Enum.Parse<UsageStep>(step)).Refusal);

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
