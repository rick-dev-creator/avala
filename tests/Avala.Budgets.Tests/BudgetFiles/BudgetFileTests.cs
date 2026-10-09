using Avala.Agents.Contracts.Connections;
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

    private static readonly ConnectionName Work = new("work");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AValidFileDeclaresCostCapsPerCurrencyATokenCapALimitThresholdAndAMemoryCap()
    {
        const string File = """{ "costPerJob": { "USD": 5, "EUR": 4.5 }, "tokensPerJob": 2000000, "holdAtLimit": 0.9, "memoryPerJobMegabytes": 4096, "carvePerChild": 0.25 }""";

        var caps = Outcomes.Succeeds(BudgetFileParser.Parse(File)).Caps;

        Assert.Equal([new Cost(5m, "USD"), new Cost(4.5m, "EUR")], caps.CostPerJob);
        Assert.Equal(
            (2_000_000L, 0.9, 4_096L, 0.25),
            (Outcomes.Present(caps.TokensPerJob), Outcomes.Present(caps.HoldAtLimit), Outcomes.Present(caps.MemoryPerJobMegabytes), Outcomes.Present(caps.CarvePerChild)));
    }

    [Fact]
    public async Task AConnectionsSectionCapsTheJobsOnThatConnectionInsteadOfTheTopLevelCapsAsync()
    {
        const string File = """{ "tokensPerJob": 1000, "connections": { "api": { "costPerJob": { "USD": 2 } } } }""";
        var committed = new CommittedFiles().With(Worktree, BudgetFile, File);
        var reader = new BudgetFileReader(committed);

        var onApi = Outcomes.Present(Outcomes.Succeeds((await reader.ReadAsync(Worktree, new ConnectionName("api"), Cancellation)).Caps));
        var onWork = Outcomes.Present(Outcomes.Succeeds((await reader.ReadAsync(Worktree, Work, Cancellation)).Caps));

        Assert.Equal([new Cost(2m, "USD")], onApi.CostPerJob);
        Assert.True(onApi.TokensPerJob.IsNone);
        Assert.Empty(onWork.CostPerJob);
        Assert.Equal(Option<long>.Some(1_000), onWork.TokensPerJob);
    }

    [Fact]
    public void AnEmptyDeclarationCapsNothing()
    {
        var caps = Outcomes.Succeeds(BudgetFileParser.Parse("{}")).Caps;

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
    [InlineData("""{ "memoryPerJobMegabytes": 0 }""", BudgetError.InvalidMemory)]
    [InlineData("""{ "memoryPerJobMegabytes": 1.5 }""", BudgetError.InvalidMemory)]
    [InlineData("""{ "memoryPerJobMegabytes": "512" }""", BudgetError.Malformed)]
    [InlineData("""{ "carvePerChild": 0 }""", BudgetError.InvalidCarve)]
    [InlineData("""{ "carvePerChild": 1 }""", BudgetError.InvalidCarve)]
    [InlineData("""{ "carvePerChild": "half" }""", BudgetError.Malformed)]
    [InlineData("""{ "connections": [] }""", BudgetError.Malformed)]
    [InlineData("""{ "connections": { " ": {} } }""", BudgetError.Malformed)]
    [InlineData("""{ "connections": { "api": { "connections": {} } } }""", BudgetError.UnknownField)]
    [InlineData("""{ "connections": { "api": { "tokensPerJob": 0 } } }""", BudgetError.InvalidTokens)]
    public void AnInvalidFileIsRejectedWithItsReason(string text, BudgetError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(BudgetFileParser.Parse(text)));

    [Fact]
    public async Task ABaseCommitWithoutABudgetFileHasNoCapsAsync()
    {
        var file = await new BudgetFileReader(new CommittedFiles().Workspace(Worktree)).ReadAsync(Worktree, Work, Cancellation);

        Assert.True(Outcomes.Succeeds(file.Caps).IsNone);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin()), file.Origin);
    }

    [Fact]
    public async Task TheBudgetFileOfTheBaseCommitIsParsedWithItsOriginAsync()
    {
        var committed = new CommittedFiles().With(Worktree, BudgetFile, """{ "tokensPerJob": 1000 }""", editedInWorktree: true);

        var file = await new BudgetFileReader(committed).ReadAsync(Worktree, Work, Cancellation);

        Assert.Equal(Option<long>.Some(1_000), Outcomes.Present(Outcomes.Succeeds(file.Caps)).TokensPerJob);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin(editedInWorktree: true)), file.Origin);
        Assert.Equal([(Worktree, BudgetFile)], committed.Reads);
    }

    [Fact]
    public async Task ABudgetFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        var committed = new CommittedFiles().With(Worktree, BudgetFile, new string(' ', BudgetFileReader.MaximumBytes + 1));

        Assert.Equal(BudgetError.TooLarge, Outcomes.FailsWith((await new BudgetFileReader(committed).ReadAsync(Worktree, Work, Cancellation)).Caps));
    }

    [Fact]
    public async Task AFolderThatIsNoJobWorkspaceHasNoCapsAndNoOriginAsync()
    {
        var file = await new BudgetFileReader(new CommittedFiles()).ReadAsync("/elsewhere", Work, Cancellation);

        Assert.True(Outcomes.Succeeds(file.Caps).IsNone);
        Assert.True(file.Origin.IsNone);
    }

    [Fact]
    public async Task ABaseCommitThatCannotBeReadIsUnreadableAsync()
    {
        var file = await new BudgetFileReader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed)).ReadAsync(Worktree, Work, Cancellation);

        Assert.Equal(BudgetError.Unreadable, Outcomes.FailsWith(file.Caps));
    }

    [Fact]
    public async Task ARepositorysCurrentBudgetListsItsCapsAndThoseOfEachConnectionAsync()
    {
        const string File = """{ "tokensPerJob": 1000, "holdAtLimit": 0.8, "connections": { "work": { "costPerJob": { "USD": 2 } }, "api": { "tokensPerJob": 50 } } }""";

        var budget = await new BudgetFileReader(new CommittedFiles().With(Repository, BudgetFile, File)).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal(
            (BudgetFileStatus.Applied, Option<BudgetError>.None, Option<long>.Some(1_000), Option<double>.Some(0.8), Option<FileOrigin>.Some(CommittedFiles.Origin())),
            (budget.File, budget.Error, budget.Caps.TokensPerJob, budget.Caps.HoldAtLimit, budget.Origin));
        Assert.Equal(
            [("api", Option<long>.Some(50), 0), ("work", Option<long>.None, 1)],
            budget.Connections.Select(connection => (connection.Connection.Value, connection.Caps.TokensPerJob, connection.Caps.CostPerJob.Count)));
    }

    [Theory]
    [InlineData(false, nameof(BudgetFileStatus.Absent), null)]
    [InlineData(true, nameof(BudgetFileStatus.Rejected), nameof(BudgetError.Malformed))]
    public async Task ARepositoryWithoutAValidBudgetCapsNothingAndSaysWhyAsync(bool declared, string status, string? error)
    {
        var committed = declared ? new CommittedFiles().With(Repository, BudgetFile, "not json") : new CommittedFiles().Workspace(Repository);

        var budget = await new BudgetFileReader(committed).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal(
            (Enum.Parse<BudgetFileStatus>(status), error is null ? Option<BudgetError>.None : Enum.Parse<BudgetError>(error), 0, true, 0),
            (budget.File, budget.Error, budget.Caps.CostPerJob.Count, budget.Caps.TokensPerJob.IsNone, budget.Connections.Count));
    }

    private const string Repository = "/repositories/shop";
}
