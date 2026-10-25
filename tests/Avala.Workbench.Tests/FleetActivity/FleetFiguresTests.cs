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
}
