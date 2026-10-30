using Avala.Workbench.FleetActivity;

namespace Avala.Workbench.Tests.FleetActivity;

public sealed class FleetFiguresTests
{
    [Theory]
    [InlineData(950, "950")]
    [InlineData(1250, "1.3K")]
    [InlineData(18_400_000, "18.4M")]
    [InlineData(2_000_000_000, "2B")]
    public void TokensAreCountedCompactly(decimal count, string shown) =>
        Assert.Equal(shown, FleetFigures.Tokens(count));

    [Theory]
    [InlineData(12.4, "12.40 USD")]
    [InlineData(0.0042, "0.0042 USD")]
    [InlineData(0, "0.00 USD")]
    public void MoneyKeepsTheDigitsThatMatter(decimal amount, string shown) =>
        Assert.Equal(shown, FleetFigures.Money(amount, "USD"));

    [Theory]
    [InlineData(1.25, 1, "▲ 25% vs the previous 7 days")]
    [InlineData(0.5, 1, "▼ 50% vs the previous 7 days")]
    [InlineData(1, 1, "no change vs the previous 7 days")]
    [InlineData(1, 0, "nothing in the previous 7 days")]
    [InlineData(0, 0, "")]
    public void AChangeSaysHowFarAFigureMoved(decimal now, decimal before, string said) =>
        Assert.Equal(said, FleetFigures.Change(now, before, "the previous 7 days"));

    [Fact]
    public void WithoutAnEarlierFigureNoChangeIsSaid() =>
        Assert.Equal(string.Empty, FleetFigures.Change(1, Avala.Sdk.Option<decimal>.None, "the previous 7 days"));

    [Fact]
    public void TheCacheHitIsTheInputReadFromCache() =>
        Assert.Equal((75m, true), (FleetFigures.CacheHit(new Avala.Fleet.Contracts.FleetTokens(10, 99, 30, 0, 0)).Match(hit => hit, () => 0), FleetFigures.CacheHit(new Avala.Fleet.Contracts.FleetTokens(0, 5, 0, 0, 0)).IsNone));

    [Fact]
    public void ARatioChangesInPoints() =>
        Assert.Equal("▼ 4 pts vs the previous 7 days", FleetFigures.PointChange(80m, 84m, "the previous 7 days"));

    [Theory]
    [InlineData(1, 4, "25%")]
    [InlineData(1, 1000, "<1%")]
    [InlineData(0, 4, "")]
    public void APartIsSaidAsAPercentOfTheWhole(decimal part, decimal whole, string said) =>
        Assert.Equal(said, FleetFigures.Percent(part, whole));
}
