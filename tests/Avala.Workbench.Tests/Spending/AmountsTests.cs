using Avala.Agents.Contracts.Events;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Tests.Spending;

public sealed class AmountsTests
{
    [Theory]
    [InlineData("5", "5 USD")]
    [InlineData("12.5", "12.50 USD")]
    [InlineData("1.844", "1.84 USD")]
    [InlineData("0.5", "0.50 USD")]
    [InlineData("0.139", "0.14 USD")]
    [InlineData("0.06", "0.06 USD")]
    [InlineData("0.079", "0.079 USD")]
    [InlineData("0.0178", "0.018 USD")]
    [InlineData("0.0048", "0.0048 USD")]
    [InlineData("0.005", "0.005 USD")]
    [InlineData("0.996", "1 USD")]
    [InlineData("0", "0 USD")]
    public void MoneyReadsWholeOrInCentsFromOneAndInTwoSignificantDigitsBelowIt(string amount, string phrase) =>
        Assert.Equal(phrase, Amounts.Costs([new Cost(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "USD")]));
}
