using Avala.Autopilot.Contracts;
using Avala.Autopilot.RepositoryFiles;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Tests.RepositoryFiles;

public sealed class LoopLimitsReaderTests : IDisposable
{
    private const string Repository = "/repositories/shop";

    private readonly TemporaryFolder data = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAMachineFileOrALimitsSectionALoopHasTheDefaultLimitsAsync() =>
        Assert.Equal(
            new LoopLimits(),
            Outcomes.Succeeds(await Reader(new CommittedFiles().With(Repository, AutopilotRulesReader.JobFile, """{ "autopilot": { "approve": "cleanEvidence" } }""")).OfRepositoryAsync(Repository, Cancellation)));

    [Fact]
    public async Task TheLimitsAreReadWhateverTheDepthOfTheOtherSectionsOfTheJobFileAsync()
    {
        var files = new CommittedFiles().With(
            Repository,
            AutopilotRulesReader.JobFile,
            """{ "autopilot": { "iterations": 20 }, "delegation": { "connections": ["work", "spare"] } }""");

        Assert.Equal(new LoopLimits { Iterations = 20 }, Outcomes.Succeeds(await Reader(files).OfRepositoryAsync(Repository, Cancellation)));
    }

    [Fact]
    public async Task TheMachineFileSetsTheLimitsAndTheRepositoryOnlyTightensThemAsync()
    {
        await File.WriteAllTextAsync(
            Path.Combine(data.Path, LoopLimitsReader.MachineFile),
            """{ "iterations": 200, "failuresInARow": 4, "pauseAtLimit": 0.8 }""",
            Cancellation);
        var files = new CommittedFiles().With(
            Repository,
            AutopilotRulesReader.JobFile,
            """{ "autopilot": { "approve": "cleanEvidence", "iterations": 20, "failuresInARow": 9, "sameFailure": 1, "pauseAtLimit": 0.95 } }""");

        var limits = Outcomes.Succeeds(await Reader(files).OfRepositoryAsync(Repository, Cancellation));

        Assert.Equal(new LoopLimits { Iterations = 20, FailuresInARow = 4, SameFailure = 1, NothingChanged = 2, PauseAtLimit = 0.8 }, limits);
    }

    [Theory]
    [InlineData("machine", "not json", "Malformed")]
    [InlineData("machine", "[]", "Malformed")]
    [InlineData("machine", """{ "iterations": 20, "approve": "cleanEvidence" }""", "UnknownField")]
    [InlineData("machine", """{ "iterations": "20" }""", "Malformed")]
    [InlineData("machine", """{ "iterations": 20, "iterations": 30 }""", "Malformed")]
    [InlineData("machine", """{ "iterations": 0 }""", "InvalidLimits")]
    [InlineData("machine", """{ "iterations": 10001 }""", "InvalidLimits")]
    [InlineData("machine", """{ "sameFailure": 1.5 }""", "InvalidLimits")]
    [InlineData("repository", """{ "autopilot": { "spendPerLoop": 5 } }""", "UnknownField")]
    [InlineData("repository", """{ "autopilot": { "failuresInARow": 101 } }""", "InvalidLimits")]
    [InlineData("repository", """{ "autopilot": { "pauseAtLimit": 1.2 } }""", "InvalidLimits")]
    [InlineData("repository", """{ "autopilot": { "nothingChanged": { "in": 2 } } }""", "Malformed")]
    [InlineData("repository", """{ "autopilot": [] }""", "Malformed")]
    public async Task AnInvalidLimitsDeclarationIsRefusedWithItsErrorAsync(string where, string text, string error)
    {
        var files = new CommittedFiles().Workspace(Repository);

        if (where == "machine")
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, LoopLimitsReader.MachineFile), text, Cancellation);
        }
        else
        {
            files.With(Repository, AutopilotRulesReader.JobFile, text);
        }

        Assert.Equal(Enum.Parse<AutopilotError>(error), Outcomes.FailsWith(await Reader(files).OfRepositoryAsync(Repository, Cancellation)));
    }

    [Fact]
    public async Task AMachineFileOverItsSizeOrAHeadThatCannotBeReadIsRefusedAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(data.Path, LoopLimitsReader.MachineFile), new string(' ', LoopLimitsParser.MaximumBytes + 1), Cancellation);
        var large = Reader(new CommittedFiles().Workspace(Repository));
        var failing = new LoopLimitsReader(new CommittedFiles().Failing(Repository, WorkspaceFailure.GitFailed), new AvalaPaths(Path.Combine(data.Path, "elsewhere")));

        Assert.Equal(AutopilotError.TooLarge, Outcomes.FailsWith(await large.OfRepositoryAsync(Repository, Cancellation)));
        Assert.Equal(AutopilotError.Unreadable, Outcomes.FailsWith(await failing.OfRepositoryAsync(Repository, Cancellation)));
    }

    public void Dispose() => data.Dispose();

    private LoopLimitsReader Reader(CommittedFiles files) => new(files, new AvalaPaths(data.Path));
}
