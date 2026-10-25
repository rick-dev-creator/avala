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
        var columns = FleetColumns.Of([Period(0, 1m), Period(1, 4m), Period(2, 2m)], FleetStep.Day, spend: true, Usd);

        Assert.Equal([0.25, 1, 0.5], columns.Select(column => column.Share));
    }

    [Fact]
    public void ARangeWithoutUsageHasFlatColumns() =>
        Assert.All(FleetColumns.Of([Period(0, 0m), Period(1, 0m)], FleetStep.Day, spend: true, Usd), column => Assert.Equal(0, column.Share));

    [Fact]
    public void TicksAreThinnedToTwelve()
    {
        var columns = FleetColumns.Of([.. Enumerable.Range(0, 30).Select(day => Period(day, 1m))], FleetStep.Day, spend: true, Usd);

        Assert.Equal(10, columns.Count(column => column.Tick.Length > 0));
    }

    [Fact]
    public void AMonthOfDaysIsTickedCompactly()
    {
        var columns = FleetColumns.Of([.. Enumerable.Range(0, 30).Select(day => Period(day, 1m))], FleetStep.Day, spend: true, Usd);

        Assert.Equal(new DateOnly(2026, 10, 1).ToString("d/M", System.Globalization.CultureInfo.CurrentCulture), columns[0].Tick);
    }

    [Fact]
    public void TokensAreChartedWhenSpendIsNot()
    {
        var columns = FleetColumns.Of([Period(0, 9m, tokens: 10), Period(1, 1m, tokens: 20)], FleetStep.Day, spend: false, Usd);

        Assert.Equal([0.5, 1], columns.Select(column => column.Share));
    }

    private static FleetPeriod Period(int day, decimal amount, decimal tokens = 0) =>
        new(new DateOnly(2026, 10, 1).AddDays(day), new FleetTokens(tokens, 0, 0, 0, 0), [new FleetCost("USD", "apiEquivalent", amount)]);
}
