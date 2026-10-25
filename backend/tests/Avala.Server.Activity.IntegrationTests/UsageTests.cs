using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Usage;
using static Avala.Server.Activity.IntegrationTests.Fleet;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class UsageTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Desktop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000a");

    private static readonly Guid Laptop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000b");

    private static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000100");

    private static readonly Guid Other = Guid.Parse("0199f6a0-0000-7000-8000-000000000200");

    private const string ShopRemote = "remote:github.com/rick/shop";

    private readonly Fleet fleet = new(database);

    [Fact]
    public async Task TheRangeKeepsOnlyItsQuartersAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, at: Noon), Usage(Guid.NewGuid(), Job, 5, at: Noon.AddDays(-2)));

        var usage = await fleet.UsageAsync("2026-10-24", "2026-10-26");

        Assert.Equal(10m, usage.Totals.Tokens.Input);
    }

    [Fact]
    public async Task QuartersFallInTheirLocalPeriodAcrossADaylightSavingChangeAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Usage(Guid.NewGuid(), Job, 10, at: new DateTimeOffset(2026, 10, 24, 21, 30, 0, TimeSpan.Zero)),
            Usage(Guid.NewGuid(), Job, 20, at: new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero)),
            Usage(Guid.NewGuid(), Job, 40, at: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));

        var usage = await fleet.UsageAsync("2026-10-24", "2026-10-26", "Europe/Madrid");

        Assert.Equal(["2026-10-24 10", "2026-10-25 60"], usage.Series.Select(period => $"{period.Start:yyyy-MM-dd} {period.Tokens.Input}"));
    }

    [Fact]
    public async Task QuartersFallInTheirLocalDayAfterASpringForwardAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Usage(Guid.NewGuid(), Job, 10, at: new DateTimeOffset(2026, 3, 28, 22, 30, 0, TimeSpan.Zero)),
            Usage(Guid.NewGuid(), Job, 20, at: new DateTimeOffset(2026, 3, 29, 22, 30, 0, TimeSpan.Zero)));

        var usage = await fleet.UsageAsync("2026-03-28", "2026-03-31", "Europe/Madrid");

        Assert.Equal(["2026-03-28 10", "2026-03-29 0", "2026-03-30 20"], usage.Series.Select(period => $"{period.Start:yyyy-MM-dd} {period.Tokens.Input}"));
    }

    [Fact]
    public async Task EmptyPeriodsAreZeroAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, at: Noon));

        var usage = await fleet.UsageAsync("2026-10-20", "2026-10-26");

        Assert.Equal([0m, 0m, 0m, 0m, 0m, 10m], usage.Series.Select(period => period.Tokens.Input));
    }

    [Fact]
    public async Task TokensAreNotCountedOncePerCostAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Spent(Job, 10, 0.1m, "USD", CostBasis.ApiEquivalent, Noon),
            Spent(Job, 20, 0.2m, "USD", CostBasis.Billed, Noon.AddMinutes(1)));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26");

        Assert.Equal((30m, 30m, 30m, 2), (usage.Totals.Tokens.Input, Assert.Single(usage.Repositories).Tokens.Input, Assert.Single(usage.Instances).Tokens.Input, usage.Totals.Costs.Count));
    }

    [Fact]
    public async Task EveryBreakdownAddsUpToTheTotalsAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Submitted(Guid.NewGuid(), Job, AvalaRemote, Noon.AddDays(-4)),
            Spent(Job, 10, 0.1m, "USD", CostBasis.ApiEquivalent, Noon.AddDays(-3)),
            Spent(Other, 20, 0.2m, "USD", CostBasis.ApiEquivalent, Noon.AddDays(-1)),
            Spent(null, 40, 0.4m, "USD", CostBasis.ApiEquivalent, Noon));

        var usage = await fleet.UsageAsync("2026-10-19", "2026-10-26");

        Assert.All(
            new[] { usage.Repositories, usage.Instances, usage.Models },
            shares => Assert.Equal(
                (usage.Totals.Tokens.Input, usage.Totals.Costs.Single().Amount),
                (shares.Sum(share => share.Tokens.Input), shares.SelectMany(share => share.Costs).Sum(cost => cost.Amount))));
    }

    [Fact]
    public async Task UsageIsAttributedToTheJobsRepositoryAtQueryTimeAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, at: Noon));
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), Job, AvalaRemote, Noon.AddMinutes(-30)));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26");

        Assert.Equal((UsageShareKind.Repository, AvalaRemote, 1), (Assert.Single(usage.Repositories).Kind, usage.Repositories[0].Key, usage.Repositories[0].Jobs));
    }

    [Fact]
    public async Task UnknownAndOutsideAnyJobHaveTheirOwnRowsAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, at: Noon), Usage(Guid.NewGuid(), null, 20, at: Noon));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26");

        Assert.Equal(
            [$"{UsageShareKind.OutsideAnyJob} 20 0", $"{UsageShareKind.Unknown} 10 1"],
            usage.Repositories.Select(share => $"{share.Kind} {share.Tokens.Input} {share.Jobs}"));
    }

    [Fact]
    public async Task JobsAreCountedPerInstanceAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, at: Noon));
        await fleet.IngestAsync(Laptop, "laptop", Usage(Guid.NewGuid(), Job, 10, at: Noon));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26");

        Assert.Equal((2, 2), (usage.Totals.Jobs, usage.Totals.Instances));
    }

    [Fact]
    public async Task AnInstanceIsNamedAndCountedOnceAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Usage(Guid.NewGuid(), Job, 10, at: Noon), Usage(Guid.NewGuid(), Other, 5, at: Noon.AddDays(-1)));

        var usage = await fleet.UsageAsync("2026-10-24", "2026-10-26");

        Assert.Equal(("desktop", 15m, 2, 1), (Assert.Single(usage.Instances).Name, usage.Instances[0].Tokens.Input, usage.Instances[0].Jobs, usage.Totals.Instances));
    }

    [Fact]
    public async Task ModelsAreSummedAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Usage(Guid.NewGuid(), Job, 10, at: Noon, model: "opus"),
            Usage(Guid.NewGuid(), Other, 30, at: Noon.AddDays(-1), model: "opus"),
            Usage(Guid.NewGuid(), Job, 5, at: Noon, model: "sonnet"));

        var usage = await fleet.UsageAsync("2026-10-24", "2026-10-26");

        Assert.Equal(["opus 40", "sonnet 5"], usage.Models.Select(model => $"{model.Key} {model.Tokens.Input}"));
    }

    [Fact]
    public async Task ARepositoryScopesEveryFigureAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Submitted(Guid.NewGuid(), Job, AvalaRemote, Noon.AddMinutes(-30)),
            Submitted(Guid.NewGuid(), Other, ShopRemote, Noon.AddMinutes(-30)),
            Usage(Guid.NewGuid(), Job, 10, 0.1m, Noon, "opus"),
            Usage(Guid.NewGuid(), Other, 30, 0.3m, Noon, "sonnet"),
            Usage(Guid.NewGuid(), null, 50, 0.5m, Noon));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26", repository: "remote:GitHub.com/Rick/Shop.git");

        Assert.Equal(
            (30m, 0.3m, 1, ShopRemote, "sonnet", 30m),
            (usage.Totals.Tokens.Input, usage.Totals.Costs.Single().Amount, usage.Totals.Jobs, Assert.Single(usage.Repositories).Key, Assert.Single(usage.Models).Key, usage.Series.Single().Tokens.Input));
    }

    [Fact]
    public async Task CostsKeepTheirCurrencyAndBasisAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Spent(Job, 10, 0.1m, "USD", CostBasis.ApiEquivalent, Noon),
            Spent(Job, 10, 0.2m, "USD", CostBasis.Billed, Noon),
            Spent(Job, 10, 0.3m, "EUR", CostBasis.Billed, Noon));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26");

        Assert.Equal(
            ["EUR Billed 0.3", "USD Billed 0.2", "USD ApiEquivalent 0.1"],
            usage.Totals.Costs.OrderBy(cost => cost.Currency, StringComparer.Ordinal).ThenBy(cost => cost.Basis).Select(cost => $"{cost.Currency} {cost.Basis} {cost.Amount:0.0}"));
    }

    [Fact]
    public async Task TheReferenceIsTheLargestCostAndOrdersTheBreakdownsAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(
            Desktop,
            "desktop",
            Submitted(Guid.NewGuid(), Job, AvalaRemote, Noon.AddMinutes(-30)),
            Submitted(Guid.NewGuid(), Other, ShopRemote, Noon.AddMinutes(-30)),
            Spent(Job, 1000, 0.1m, "USD", CostBasis.ApiEquivalent, Noon),
            Spent(Other, 10, 0.9m, "USD", CostBasis.ApiEquivalent, Noon),
            Spent(Other, 10, 0.5m, "EUR", CostBasis.Billed, Noon));

        var usage = await fleet.UsageAsync("2026-10-25", "2026-10-26");

        Assert.Equal(
            (new CostKind("USD", CostBasis.ApiEquivalent), ShopRemote),
            (usage.Reference, usage.Repositories[0].Key));
    }
}
