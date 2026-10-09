using Avala.Agents.Contracts.Events;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.Tests.BudgetFiles;

public sealed class BudgetFileTests
{
    private const string Worktree = "/worktrees/1";
    private const string BudgetFile = ".avala/budget.json";

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
    public async Task ABaseCommitWithoutABudgetFileHasNoCapsAsync()
    {
        var file = await new BudgetFileReader(new CommittedFiles().Workspace(Worktree)).ReadAsync(Worktree, Cancellation);

        Assert.True(Outcomes.Succeeds(file.Caps).IsNone);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin()), file.Origin);
    }

    [Fact]
    public async Task TheBudgetFileOfTheBaseCommitIsParsedWithItsOriginAsync()
    {
        var committed = new CommittedFiles().With(Worktree, BudgetFile, """{ "tokensPerJob": 1000 }""", editedInWorktree: true);

        var file = await new BudgetFileReader(committed).ReadAsync(Worktree, Cancellation);

        Assert.Equal(Option<long>.Some(1_000), Outcomes.Present(Outcomes.Succeeds(file.Caps)).TokensPerJob);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin(editedInWorktree: true)), file.Origin);
        Assert.Equal([(Worktree, BudgetFile)], committed.Reads);
    }

    [Fact]
    public async Task ABudgetFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        var committed = new CommittedFiles().With(Worktree, BudgetFile, new string(' ', BudgetFileReader.MaximumBytes + 1));

        Assert.Equal(BudgetError.TooLarge, Outcomes.FailsWith((await new BudgetFileReader(committed).ReadAsync(Worktree, Cancellation)).Caps));
    }

    [Fact]
    public async Task AFolderThatIsNoJobWorkspaceHasNoCapsAndNoOriginAsync()
    {
        var file = await new BudgetFileReader(new CommittedFiles()).ReadAsync("/elsewhere", Cancellation);

        Assert.True(Outcomes.Succeeds(file.Caps).IsNone);
        Assert.True(file.Origin.IsNone);
    }

    [Fact]
    public async Task ABaseCommitThatCannotBeReadIsUnreadableAsync()
    {
        var file = await new BudgetFileReader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed)).ReadAsync(Worktree, Cancellation);

        Assert.Equal(BudgetError.Unreadable, Outcomes.FailsWith(file.Caps));
    }
}
