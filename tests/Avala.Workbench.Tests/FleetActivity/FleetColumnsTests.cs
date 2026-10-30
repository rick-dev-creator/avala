using Avala.Fleet.Contracts;
using Avala.Sdk;
using Avala.Workbench.FleetActivity;

namespace Avala.Workbench.Tests.FleetActivity;

public sealed class FleetColumnsTests
{
    private static readonly FleetCostKind Usd = new("USD", "apiEquivalent");

    [Fact]
    public void EveryColumnIsItsShareOfTheHighest()
    {
        var columns = FleetColumns.Of([Period(0, 1m), Period(1, 4m), Period(2, 2m)], FleetStep.Day, spend: true, Usd, []);

        Assert.Equal([0.25, 1, 0.5], columns.Select(column => column.Share));
    }

    [Fact]
    public void ARangeWithoutUsageHasFlatColumns() =>
        Assert.All(FleetColumns.Of([Period(0, 0m), Period(1, 0m)], FleetStep.Day, spend: true, Usd, []), column => Assert.Equal(0, column.Share));

    [Fact]
    public void TicksAreThinnedToTwelve()
    {
        var columns = FleetColumns.Of([.. Enumerable.Range(0, 30).Select(day => Period(day, 1m))], FleetStep.Day, spend: true, Usd, []);

        Assert.Equal(10, columns.Count(column => column.Tick.Length > 0));
    }

    [Fact]
    public void AMonthOfDaysIsTickedCompactly()
    {
        var columns = FleetColumns.Of([.. Enumerable.Range(0, 30).Select(day => Period(day, 1m))], FleetStep.Day, spend: true, Usd, []);

        Assert.Equal(new DateOnly(2026, 10, 1).ToString("d/M", System.Globalization.CultureInfo.CurrentCulture), columns[0].Tick);
    }

    [Fact]
    public void TokensAreChartedWhenSpendIsNot()
    {
        var columns = FleetColumns.Of([Period(0, 9m, tokens: 10), Period(1, 1m, tokens: 20)], FleetStep.Day, spend: false, Usd, []);

        Assert.Equal([0.5, 1], columns.Select(column => column.Share));
    }

    [Fact]
    public void ThePreviousRangeIsMarkedOnTheSameScale()
    {
        var columns = FleetColumns.Of([Period(0, 1m), Period(1, 2m)], FleetStep.Day, spend: true, Usd, [Period(-7, 4m), Period(-6, 1m)]);

        Assert.Equal([(0.25, 1), (0.5, 0.25)], columns.Select(column => (column.Share, column.Previous.Match(share => share, () => -1))));
    }

    [Fact]
    public void AColumnSaysWhatItsPreviousPeriodHad()
    {
        var columns = FleetColumns.Of([Period(0, 1m)], FleetStep.Day, spend: true, Usd, [Period(-7, 4m)]);

        Assert.EndsWith($"{FleetFigures.Period(new DateOnly(2026, 9, 24), FleetStep.Day)} had 0 tokens · {FleetFigures.Money(4m, "USD")} at API prices", columns[0].Tip, StringComparison.Ordinal);
    }

    [Fact]
    public void TokensAreStackedByType()
    {
        var period = new FleetPeriod(new DateOnly(2026, 10, 1), new FleetTokens(10, 20, 50, 15, 5), []);

        var column = FleetColumns.Of([period], FleetStep.Day, spend: false, Usd, [])[0];

        Assert.Equal((1, new FleetStack(0.1, 0.3, 0.35, 0.5)), (column.Share, column.Stack));
    }

    private static FleetPeriod Period(int day, decimal amount, decimal tokens = 0) =>
        new(new DateOnly(2026, 10, 1).AddDays(day), new FleetTokens(tokens, 0, 0, 0, 0), [new FleetCost("USD", "apiEquivalent", amount)]);
}
