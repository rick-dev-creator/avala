using System.Globalization;
using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Domain;

public sealed class AdmissionTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_000_001)]
    public void TokenCountsOutsideTheirBoundsAreRefused(long count) =>
        Assert.False(TokenCounts.Create(0, 0, count, 0, 0).IsAdmitted);

    [Theory]
    [InlineData("1000000.000001", "USD")]
    [InlineData("-0.01", "USD")]
    [InlineData("0.0000001", "USD")]
    [InlineData("1.00", "usd")]
    [InlineData("1.00", "US")]
    public void ACostAboveAMillionOrWithMoreThanSixDecimalsOrAnotherCurrencyShapeIsRefused(string amount, string currency) =>
        Assert.False(CostAmount.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), currency, CostBasis.Billed).IsAdmitted);

    [Fact]
    public void ACostWithSixDecimalsIsAdmitted() =>
        Assert.Equal(0.123456m, CostAmount.Create(0.123456m, "USD", CostBasis.ApiEquivalent).Value.Amount);

    [Fact]
    public void AModelNameLongerThanAHundredCharactersIsRefused() =>
        Assert.False(ModelChoice.Create(new string('m', 101), effort: null).IsAdmitted);

    [Theory]
    [InlineData("op\0us")]
    [InlineData("op\u0007us")]
    public void AModelNameWithAControlCharacterIsRefused(string model) =>
        Assert.False(ModelChoice.Create(model, effort: null).IsAdmitted);

    [Fact]
    public void AModelNameWithALoneSurrogateIsRefused() =>
        Assert.False(ModelChoice.Create($"op{(char)0xD800}us", effort: null).IsAdmitted);

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void AnAttemptOutsideItsBoundsIsRefused(int number) =>
        Assert.False(Attempt.Create(number, VerificationOutcome.Passed).IsAdmitted);
}
