using Avala.Components.Privacy;
using Avala.Fleet.Contracts;
using Avala.Sdk;
using Avala.Workbench.FleetActivity;

namespace Avala.Workbench.Tests.FleetActivity;

public sealed class FleetTileViewModelScripts
{
    [Fact]
    public void TheSpendTileNamesTheOtherCurrencies()
    {
        var usage = FakeFleetActivity.Fleet() with
        {
            Totals = new FleetTotals(new FleetTokens(0, 0, 0, 0, 0), [new FleetCost("USD", "apiEquivalent", 1.25m), new FleetCost("EUR", "billed", 0.5m)], 0, 0),
        };

        Assert.Equal($"at API prices · also {FleetFigures.Money(0.5m, "EUR")} billed", FleetTileViewModel.Of(usage)[0].Caption);
    }

    [Fact]
    public void WithoutACostTheSpendTileSaysSo() =>
        Assert.Equal("no cost reported", FleetTileViewModel.Of(FakeFleetActivity.Fleet(costed: false))[0].Caption);
}

public sealed class FleetTrendViewModelScripts
{
    [Fact]
    public void TheAxisNamesTheHighestPeriod()
    {
        var trend = new FleetTrendViewModel();

        trend.Show(FakeFleetActivity.Fleet(), FleetStep.Day);

        Assert.Equal($"{FleetFigures.Money(0.75m, "USD")} at API prices, the highest day", trend.Axis);
    }

    [Fact]
    public void SpendCannotBeChosenWithoutCosts()
    {
        var trend = new FleetTrendViewModel();
        trend.Show(FakeFleetActivity.Fleet(costed: false), FleetStep.Day);

        trend.ShowSpendCommand.Execute(null);

        Assert.Equal((false, false), (trend.CanShowSpend, trend.ShowsSpend));
    }
}

public sealed class FleetColumnViewModelScripts
{
    [Fact]
    public void AColumnKeepsItsShareTickAndTip()
    {
        var column = new FleetColumnViewModel(0.5, "19 Oct", "Mon 19 Oct · 1K tokens");

        Assert.Equal((0.5, "19 Oct", "Mon 19 Oct · 1K tokens"), (column.Share, column.Tick, column.Tip));
    }
}

public sealed class FleetShareListViewModelScripts
{
    [Fact]
    public void EveryRowIsItsShareOfTheHighest()
    {
        var list = new FleetShareListViewModel("Repositories");

        list.Show(FakeFleetActivity.Fleet().Repositories, spend: true, FakeFleetActivity.Usd, Option<string>.None, _ => Task.CompletedTask);

        Assert.Equal([1, 0.95, 0.01, 0.005], list.Rows.Select(row => Math.Round(row.Share, 3)));
    }
}

public sealed class FleetShareListOrderScripts
{
    [Fact]
    public void ByTokensTheBiggestTokenUserComesFirstWhateverItCost()
    {
        var list = new FleetShareListViewModel("Repositories");
        FleetShare Share(string key, decimal tokens, decimal cost) =>
            new(FleetShareKind.Repository, key, key, new FleetTokens(tokens, 0, 0, 0, 0), [new FleetCost("USD", "apiEquivalent", cost)], 1);

        list.Show([Share("costly", 10, 5m), Share("busy", 1000, 0.1m)], spend: false, FakeFleetActivity.Usd, Option<string>.None, _ => Task.CompletedTask);

        Assert.Equal(["busy", "costly"], list.Rows.Select(row => row.Name.Value));
    }
}

public sealed class FleetShareRowViewModelScripts
{
    [Theory]
    [InlineData(FleetShareKind.Unknown, "Repository not known yet", PersonalKind.None)]
    [InlineData(FleetShareKind.OutsideAnyJob, "Outside any job", PersonalKind.None)]
    [InlineData(FleetShareKind.Repository, "github.com/rick/avala", PersonalKind.AccountName)]
    public void ARowIsNamedForWhatItCounts(FleetShareKind kind, string name, PersonalKind personal)
    {
        var row = new FleetShareRowViewModel(
            new FleetShareRow(new FleetShare(kind, kind == FleetShareKind.Repository ? $"remote:{name}" : string.Empty, string.Empty, new FleetTokens(0, 0, 0, 0, 0), [], 0), 1, string.Empty, Chosen: false),
            _ => Task.CompletedTask);

        Assert.Equal((name, personal), (row.Name.Value, row.Name.Kind));
    }
}

public sealed class FleetRangeViewModelScripts
{
    [Fact]
    public void CustomOpensOnTheDatesShown()
    {
        var range = new FleetRangeViewModel(() => new DateOnly(2026, 10, 25));

        range.Options[6].ChooseCommand.Execute(null);

        Assert.Equal((true, "2026-10-19", "2026-10-25"), (range.IsCustomOpen, range.CustomFrom, range.CustomTo));
    }

    [Fact]
    public void AnUnreadableDateIsRefused()
    {
        var range = new FleetRangeViewModel(() => new DateOnly(2026, 10, 25));
        range.CustomFrom = "19/10/2026";

        range.ApplyCustomCommand.Execute(null);

        Assert.Equal("Write the dates as 2026-10-01", range.Refusal);
    }
}

public sealed class FleetRangeOptionViewModelScripts
{
    [Fact]
    public void ChoosingAnOptionReportsItsChoice()
    {
        var chosen = new List<FleetRangeChoice>();
        var option = new FleetRangeOptionViewModel(FleetRangeChoice.SixMonths, "6 months", chosen.Add);

        option.ChooseCommand.Execute(null);

        Assert.Equal([FleetRangeChoice.SixMonths], chosen);
    }
}

public sealed class FleetRepositoryJobsViewModelScripts
{
    [Fact]
    public void TheJobsAreListedNewestFirst()
    {
        var jobs = new FleetRepositoryJobsViewModel();
        FleetJob Job(string instance, int hour) =>
            new(Guid.NewGuid(), instance, Guid.NewGuid(), "running", string.Empty, 0, string.Empty, new FleetTokens(0, 0, 0, 0, 0), [], FakeFleetActivity.Noon.AddHours(hour));

        jobs.Show("remote:github.com/rick/avala", new FleetJobsView(FleetReadState.Read, [Job("desktop", 0), Job("laptop", 1)]));

        Assert.Equal(["laptop", "desktop"], jobs.Jobs.Select(job => job.Instance.Value));
    }
}

public sealed class FleetJobRowViewModelScripts
{
    [Fact]
    public void AJobShowsItsStateHoldAndAttempts()
    {
        var row = new FleetJobRowViewModel(new FakeFleetActivity().Jobs.Jobs[0]);

        Assert.StartsWith("needsHelp · held: budgetExceeded · 2 attempts, last failed", row.State, StringComparison.Ordinal);
    }
}
