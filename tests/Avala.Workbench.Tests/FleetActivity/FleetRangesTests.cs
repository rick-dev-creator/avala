using Avala.Fleet.Contracts;
using Avala.Workbench.FleetActivity;

namespace Avala.Workbench.Tests.FleetActivity;

public sealed class FleetRangesTests
{
    private static readonly DateOnly Sunday = new(2026, 10, 25);

    public static TheoryData<string, string, string, string, string> Choices => new()
    {
        { nameof(FleetRangeChoice.LastSevenDays), "2026-10-25", "2026-10-19", "2026-10-26", nameof(FleetStep.Day) },
        { nameof(FleetRangeChoice.LastThirtyDays), "2026-10-25", "2026-09-26", "2026-10-26", nameof(FleetStep.Day) },
        { nameof(FleetRangeChoice.ThisMonth), "2026-10-25", "2026-10-01", "2026-10-26", nameof(FleetStep.Day) },
        { nameof(FleetRangeChoice.ThisMonth), "2026-10-01", "2026-10-01", "2026-10-02", nameof(FleetStep.Day) },
        { nameof(FleetRangeChoice.ThreeMonths), "2026-10-25", "2026-07-27", "2026-10-26", nameof(FleetStep.Week) },
        { nameof(FleetRangeChoice.ThreeMonths), "2026-10-19", "2026-07-27", "2026-10-26", nameof(FleetStep.Week) },
        { nameof(FleetRangeChoice.SixMonths), "2026-10-25", "2026-04-27", "2026-10-26", nameof(FleetStep.Week) },
        { nameof(FleetRangeChoice.TwelveMonths), "2026-10-25", "2025-11-01", "2026-11-01", nameof(FleetStep.Month) },
        { nameof(FleetRangeChoice.TwelveMonths), "2027-01-15", "2026-02-01", "2027-02-01", nameof(FleetStep.Month) },
    };

    [Theory]
    [MemberData(nameof(Choices))]
    public void EachChoiceCoversItsDatesWithItsStep(string choice, string today, string from, string to, string step)
    {
        var range = FleetRanges.Of(Enum.Parse<FleetRangeChoice>(choice), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal((from, to, step), (Iso(range.From), Iso(range.To), range.Step.ToString()));
    }

    public static TheoryData<string, string, string, string, string> Earlier => new()
    {
        { nameof(FleetRangeChoice.LastSevenDays), "2026-10-25", "2026-10-12", "2026-10-19", "the previous 7 days" },
        { nameof(FleetRangeChoice.ThisMonth), "2026-10-25", "2026-09-06", "2026-10-01", "the previous 25 days" },
        { nameof(FleetRangeChoice.ThisMonth), "2026-10-01", "2026-09-30", "2026-10-01", "the previous day" },
        { nameof(FleetRangeChoice.ThreeMonths), "2026-10-25", "2026-04-27", "2026-07-27", "the previous 13 weeks" },
        { nameof(FleetRangeChoice.TwelveMonths), "2026-10-25", "2024-11-01", "2025-11-01", "the previous 12 months" },
    };

    [Theory]
    [MemberData(nameof(Earlier))]
    public void EachRangeIsComparedWithTheSameLengthBeforeIt(string choice, string today, string from, string to, string named)
    {
        var range = FleetRanges.Of(Enum.Parse<FleetRangeChoice>(choice), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));

        var before = FleetRanges.Before(range);

        Assert.Equal((from, to, range.Step, named), (Iso(before.From), Iso(before.To), before.Step, FleetRanges.Previous(range)));
    }

    [Fact]
    public void ACustomRangeUpToSixtyTwoDaysIsCountedByDay()
    {
        var range = Custom(new DateOnly(2026, 8, 25), Sunday);

        Assert.Equal(("2026-08-25", "2026-10-26", FleetStep.Day), (Iso(range.From), Iso(range.To), range.Step));
    }

    [Fact]
    public void ALongerCustomRangeIsWidenedToWholeWeeks()
    {
        var range = Custom(new DateOnly(2026, 8, 20), new DateOnly(2026, 10, 22));

        Assert.Equal(("2026-08-17", "2026-10-26", FleetStep.Week), (Iso(range.From), Iso(range.To), range.Step));
    }

    [Fact]
    public void ACustomRangeEndsToday()
    {
        var range = Custom(new DateOnly(2026, 10, 20), new DateOnly(2026, 12, 31));

        Assert.Equal("2026-10-26", Iso(range.To));
    }

    [Theory]
    [InlineData("2026-10-20", "2026-10-19", nameof(FleetRangeRefusal.EndBeforeStart))]
    [InlineData("2025-10-24", "2026-10-25", nameof(FleetRangeRefusal.LongerThanAYear))]
    [InlineData("2026-10-27", "2026-10-30", nameof(FleetRangeRefusal.StartAfterToday))]
    public void ACustomRangeThatCannotBeShownIsRefused(string from, string to, string refusal) =>
        Assert.Equal(
            Enum.Parse<FleetRangeRefusal>(refusal),
            FleetRanges.Custom(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture), Sunday)
                .Match(_ => (FleetRangeRefusal)(-1), found => found));

    private static FleetRange Custom(DateOnly from, DateOnly to) =>
        FleetRanges.Custom(from, to, Sunday).Match(range => range, refusal => throw new InvalidOperationException(refusal.ToString()));

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
