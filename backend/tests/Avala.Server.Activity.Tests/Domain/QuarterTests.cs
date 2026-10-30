using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Domain;

public sealed class QuarterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AQuarterIsTheTimeTruncatedToFifteenMinutesInUtc() =>
        Assert.Equal(Noon.AddMinutes(15), UsageQuarter.Of(new EventTime(Noon.AddMinutes(29).AddSeconds(59), Clamped: false)));

    [Fact]
    public void TheQuarterOfAnOffsetTimeIsItsUtcQuarter()
    {
        var quarter = UsageQuarter.Of(new EventTime(new DateTimeOffset(2026, 10, 25, 17, 50, 0, TimeSpan.FromMinutes(345)), Clamped: false));

        Assert.Equal((new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), TimeSpan.Zero), (quarter, quarter.Offset));
    }

    [Fact]
    public void ATimeOnAQuarterIsItsOwnQuarter() =>
        Assert.Equal(Noon.AddMinutes(45), UsageQuarter.Of(new EventTime(Noon.AddMinutes(45), Clamped: false)));

    [Fact]
    public void SpendingAddsTokensAndCostPerCurrencyAndBasis()
    {
        var bucket = QuarterUsage.Start(Guid.NewGuid(), Guid.NewGuid(), Noon, "opus");

        bucket.Spend(new TokenCounts(10, 1, 0, 0, 0), new CostAmount(0.5m, "USD", CostBasis.Billed));
        bucket.Spend(new TokenCounts(5, 2, 0, 0, 0), new CostAmount(0.25m, "USD", CostBasis.Billed));
        bucket.Spend(new TokenCounts(1, 0, 0, 0, 0), new CostAmount(2m, "USD", CostBasis.ApiEquivalent));

        Assert.Equal(
            (16m, 3m, "Billed 0.75, ApiEquivalent 2"),
            (bucket.Tokens.Input, bucket.Tokens.Output, string.Join(", ", bucket.Costs.Select(cost => $"{cost.Basis} {cost.Amount}"))));
    }

    [Fact]
    public void AnEmptyJobIdIsRefused() =>
        Assert.Equal("the job id is empty", JobIds.Admit(Guid.Empty).Refusal);

    [Theory]
    [InlineData("  opus ", null, "opus", null)]
    [InlineData(null, " high  ", null, "high")]
    public void AModelNameAndEffortAreTrimmed(string? model, string? effort, string? trimmedModel, string? trimmedEffort) =>
        Assert.Equal(new ModelChoice(trimmedModel, trimmedEffort), ModelChoice.Create(model, effort).Value);

    [Fact]
    public void ANameThatFitsOnceTrimmedIsAdmitted() =>
        Assert.True(ModelChoice.Create($"  {new string('m', 100)}  ", effort: null).IsAdmitted);
}
