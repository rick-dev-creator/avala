using Avala.Agents.Contracts.Events;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Budgets.Tests.BudgetFiles;

public sealed class BudgetFileTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AValidFileDeclaresCostCapsPerCurrencyATokenCapAndALimitThreshold()
    {
        const string File = """{ "costPerJob": { "USD": 5, "EUR": 4.5 }, "tokensPerJob": 2000000, "holdAtLimit": 0.9 }""";

        var caps = Outcomes.Succeeds(BudgetFileParser.Parse(File));

        Assert.Equal([new Cost(5m, "USD"), new Cost(4.5m, "EUR")], caps.CostPerJob);
        Assert.Equal((2_000_000L, 0.9), (Outcomes.Present(caps.TokensPerJob), Outcomes.Present(caps.HoldAtLimit)));
    }

    [Fact]
    public void AnEmptyDeclarationCapsNothing()
    {
        var caps = Outcomes.Succeeds(BudgetFileParser.Parse("{}"));

        Assert.Equal((0, true, true), (caps.CostPerJob.Count, caps.TokensPerJob.IsNone, caps.HoldAtLimit.IsNone));
    }

    [Theory]
    [InlineData("not json", BudgetError.Malformed)]
    [InlineData("[]", BudgetError.Malformed)]
    [InlineData("""{ "costPerJob": [5] }""", BudgetError.Malformed)]
    [InlineData("""{ "costPerJob": { "USD": "5" } }""", BudgetError.Malformed)]
    [InlineData("""{ "tokensPerJob": 1, "tokensPerJob": 2 }""", BudgetError.Malformed)]
    [InlineData("""{ "costPerJob": { "USD": { "amount": 5 } } }""", BudgetError.Malformed)]
    [InlineData("""{ "costPerTurn": { "USD": 5 } }""", BudgetError.UnknownField)]
    [InlineData("""{ "costPerJob": { "USD": 0 } }""", BudgetError.InvalidCost)]
    [InlineData("""{ "costPerJob": { " ": 5 } }""", BudgetError.InvalidCost)]
    [InlineData("""{ "tokensPerJob": 0 }""", BudgetError.InvalidTokens)]
    [InlineData("""{ "tokensPerJob": 1.5 }""", BudgetError.InvalidTokens)]
    [InlineData("""{ "holdAtLimit": 0 }""", BudgetError.InvalidThreshold)]
    [InlineData("""{ "holdAtLimit": 1.2 }""", BudgetError.InvalidThreshold)]
    public void AnInvalidFileIsRejectedWithItsReason(string text, BudgetError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(BudgetFileParser.Parse(text)));

    [Fact]
    public async Task AWorkingDirectoryWithoutABudgetFileHasNoCapsAsync()
    {
        using var folder = new TemporaryFolder();

        Assert.True(Outcomes.Succeeds(await new BudgetFileReader().ReadAsync(folder.Path, Cancellation)).IsNone);
    }

    [Fact]
    public async Task TheBudgetFileOfTheWorkingDirectoryIsReadAndParsedAsync()
    {
        using var folder = new TemporaryFolder();
        await WriteAsync(folder, """{ "tokensPerJob": 1000 }""");

        var caps = Outcomes.Present(Outcomes.Succeeds(await new BudgetFileReader().ReadAsync(folder.Path, Cancellation)));

        Assert.Equal(Option<long>.Some(1_000), caps.TokensPerJob);
    }

    [Fact]
    public async Task ABudgetFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        using var folder = new TemporaryFolder();
        await WriteAsync(folder, new string(' ', BudgetFileReader.MaximumBytes + 1));

        Assert.Equal(BudgetError.TooLarge, Outcomes.FailsWith(await new BudgetFileReader().ReadAsync(folder.Path, Cancellation)));
    }

    private static async Task WriteAsync(TemporaryFolder folder, string content)
    {
        Directory.CreateDirectory(Path.Combine(folder.Path, ".avala"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, ".avala", "budget.json"), content, Cancellation);
    }
}
