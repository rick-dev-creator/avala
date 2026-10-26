using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Usage;
using CostBasis = Avala.Server.Activity.Contracts.CostBasis;

namespace Avala.Server.Activity.Tests.Infrastructure;

public sealed class UsageAssemblyTests
{
    private const string AvalaRemote = "remote:github.com/rick/avala";

    private const string ShopRemote = "remote:github.com/rick/shop";

    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly UsageRows Nothing = new([], [], [], [], [], new Dictionary<Guid, string>());

    [Fact]
    public void EmptyPeriodsAreZero()
    {
        var report = UsageAssembly.Report(Request("2026-10-20", "2026-10-26"), Nothing with { QuarterTokens = [new QuarterTokens(Noon, Tokens(10))] });

        Assert.Equal([0m, 0m, 0m, 0m, 0m, 10m], report.Series.Select(period => period.Tokens.Input));
    }

    [Fact]
    public void TheReferenceIsTheLargestCostAndOrdersTheBreakdowns()
    {
        var rows = Nothing with
        {
            QuarterCosts = [new QuarterCost(Noon, Cost("USD", CostBasis.ApiEquivalent, 1.0m)), new QuarterCost(Noon, Cost("EUR", CostBasis.Billed, 0.5m))],
            ShareTokens = [new ShareTokens(UsageShareKind.Repository, AvalaRemote, Tokens(1000)), new ShareTokens(UsageShareKind.Repository, ShopRemote, Tokens(20))],
            ShareCosts =
            [
                new ShareCost(UsageShareKind.Repository, AvalaRemote, Cost("USD", CostBasis.ApiEquivalent, 0.1m)),
                new ShareCost(UsageShareKind.Repository, ShopRemote, Cost("USD", CostBasis.ApiEquivalent, 0.9m)),
                new ShareCost(UsageShareKind.Repository, ShopRemote, Cost("EUR", CostBasis.Billed, 0.5m)),
            ],
        };

        var report = UsageAssembly.Report(Request("2026-10-25", "2026-10-26"), rows);

        Assert.Equal((new CostKind("USD", CostBasis.ApiEquivalent), ShopRemote), (report.Reference, report.Repositories[0].Key));
    }

    private static UsageRequest Request(string from, string to) => UsageRequests.Read(from, to, "UTC", "day", repository: null).Request!;

    private static TokenSums Tokens(decimal input) => new(input, 0, 0, 0, 0);

    private static CostSum Cost(string currency, CostBasis basis, decimal amount) => new(currency, basis, amount);
}
