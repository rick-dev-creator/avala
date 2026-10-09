using Avala.Autopilot.Backlogs;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.RepositoryFiles;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Tests.RepositoryFiles;

public sealed class RepositoryFileTests
{
    private const string Worktree = "/worktrees/job-1";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{ "approval": "merge" }""", "Never", "Refuse")]
    [InlineData("""{ "autopilot": {} }""", "Never", "Refuse")]
    [InlineData("""{ "approval": "merge", "autopilot": { "approve": "cleanEvidence", "followUps": "accept" } }""", "CleanEvidence", "Accept")]
    [InlineData("""{ "autopilot": { "approve": "never", "followUps": "refuse" } }""", "Never", "Refuse")]
    public async Task TheAutopilotSectionOfTheJobFileOfTheBaseCommitDeclaresTheRulesAsync(string text, string approve, string followUps)
    {
        var files = new CommittedFiles().With(Worktree, AutopilotRulesReader.JobFile, text);

        var rules = Outcomes.Succeeds(await new AutopilotRulesReader(files).OfWorktreeAsync(Worktree, Cancellation));

        Assert.Equal(new AutopilotRules(Enum.Parse<ApprovalRule>(approve), Enum.Parse<FollowUpRule>(followUps)), rules);
    }

    [Fact]
    public async Task WithoutAJobFileNothingIsApprovedAutomaticallyAndNoFollowUpIsAcceptedAsync() =>
        Assert.Equal(
            AutopilotRules.Default,
            Outcomes.Succeeds(await new AutopilotRulesReader(new CommittedFiles().Workspace(Worktree)).OfWorktreeAsync(Worktree, Cancellation)));

    [Theory]
    [InlineData("not json", "Malformed")]
    [InlineData("""{ "autopilot": "on" }""", "Malformed")]
    [InlineData("""{ "autopilot": { "approve": true } }""", "Malformed")]
    [InlineData("""{ "autopilot": { "approve": "always" } }""", "UnknownRule")]
    [InlineData("""{ "autopilot": { "followUps": "sometimes" } }""", "UnknownRule")]
    [InlineData("""{ "autopilot": { "approve": "never", "approve": "cleanEvidence" } }""", "Malformed")]
    [InlineData("""{ "autopilot": { "approve": "never", "breakers": 3 } }""", "UnknownField")]
    [InlineData("""{ "autopilot": { "approve": { "when": "clean" } } }""", "Malformed")]
    public async Task AnInvalidAutopilotSectionIsRejectedWithItsErrorAsync(string text, string error)
    {
        var files = new CommittedFiles().With(Worktree, AutopilotRulesReader.JobFile, text);

        Assert.Equal(Enum.Parse<AutopilotError>(error), Outcomes.FailsWith(await new AutopilotRulesReader(files).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task AJobFileOverItsSizeOrABaseCommitThatCannotBeReadIsRejectedAsync()
    {
        var large = new CommittedFiles().With(Worktree, AutopilotRulesReader.JobFile, $$"""{ "approval": "{{new string('a', AutopilotRulesReader.MaximumBytes)}}" }""");
        var failing = new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed);

        Assert.Equal(AutopilotError.TooLarge, Outcomes.FailsWith(await new AutopilotRulesReader(large).OfWorktreeAsync(Worktree, Cancellation)));
        Assert.Equal(AutopilotError.Unreadable, Outcomes.FailsWith(await new AutopilotRulesReader(failing).OfWorktreeAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task TheBacklogOfTheRepositorysCurrentBaseListsItsTasksAndItsRecurringTasksInOrderAsync()
    {
        const string Backlog = """
            {
              "tasks": [
                { "id": "greet", "instruction": "Greet the team" },
                { "id": "docs.1", "instruction": "Document the greeting" }
              ],
              "recurring": [
                { "id": "deps", "instruction": "Update the dependencies", "everyMinutes": 1440 }
              ]
            }
            """;
        var files = new CommittedFiles().With("/repositories/shop", BacklogFileReader.BacklogFile, Backlog);

        var backlog = Outcomes.Succeeds(await new BacklogFileReader(files).CurrentAsync("/repositories/shop", Cancellation));

        Assert.Equal([new BacklogTask("greet", "Greet the team"), new BacklogTask("docs.1", "Document the greeting")], backlog.Tasks);
        Assert.Equal([new RecurringTask("deps", "Update the dependencies", TimeSpan.FromDays(1))], backlog.Recurring);
        Assert.Equal([("/repositories/shop", BacklogFileReader.BacklogFile)], files.Reads);
    }

    [Fact]
    public async Task ARepositoryWithoutABacklogHasNothingToDoAsync() =>
        Assert.Equal(
            BacklogDeclaration.Empty,
            Outcomes.Succeeds(await new BacklogFileReader(new CommittedFiles().Workspace("/repositories/shop")).CurrentAsync("/repositories/shop", Cancellation)));

    [Theory]
    [InlineData("[]", "Malformed")]
    [InlineData("""{ "tasks": {} }""", "Malformed")]
    [InlineData("""{ "tasks": [ "Greet the team" ] }""", "Malformed")]
    [InlineData("""{ "todo": [] }""", "UnknownField")]
    [InlineData("""{ "tasks": [ { "id": "greet", "instruction": "Greet", "priority": 1 } ] }""", "UnknownField")]
    [InlineData("""{ "tasks": [ { "instruction": "Greet" } ] }""", "InvalidKey")]
    [InlineData("""{ "tasks": [ { "id": "-greet", "instruction": "Greet" } ] }""", "InvalidKey")]
    [InlineData("""{ "tasks": [ { "id": "greet\n", "instruction": "Greet" } ] }""", "InvalidKey")]
    [InlineData("""{ "tasks": [ { "id": "greet" } ] }""", "MissingInstruction")]
    [InlineData("""{ "tasks": [ { "id": "greet", "instruction": " " } ] }""", "MissingInstruction")]
    [InlineData("""{ "tasks": [ { "id": "greet", "instruction": 1 } ] }""", "Malformed")]
    [InlineData("""{ "tasks": [ { "id": "a", "instruction": "A" } ], "recurring": [ { "id": "a", "instruction": "B", "everyMinutes": 5 } ] }""", "DuplicateKey")]
    [InlineData("""{ "recurring": [ { "id": "deps", "instruction": "Update" } ] }""", "InvalidInterval")]
    [InlineData("""{ "recurring": [ { "id": "deps", "instruction": "Update", "everyMinutes": 0 } ] }""", "InvalidInterval")]
    [InlineData("""{ "recurring": [ { "id": "deps", "instruction": "Update", "everyMinutes": 525601 } ] }""", "InvalidInterval")]
    [InlineData("""{ "recurring": [ { "id": "deps", "instruction": "Update", "everyMinutes": "daily" } ] }""", "Malformed")]
    public void AnInvalidBacklogIsRejectedWithItsError(string text, string error) =>
        Assert.Equal(Enum.Parse<AutopilotError>(error), Outcomes.FailsWith(BacklogFileReader.Parse(text)));

    [Fact]
    public async Task ABacklogOverItsSizeOrARepositoryThatCannotBeReadIsRejectedAsync()
    {
        var failing = new CommittedFiles().Failing("/repositories/shop", WorkspaceFailure.NotAGitRepository);

        Assert.Equal(AutopilotError.TooLarge, Outcomes.FailsWith(BacklogFileReader.Parse($$"""{ "tasks": [ { "id": "a", "instruction": "{{new string('a', BacklogFileReader.MaximumBytes)}}" } ] }""")));
        Assert.Equal(AutopilotError.Unreadable, Outcomes.FailsWith(await new BacklogFileReader(failing).CurrentAsync("/repositories/shop", Cancellation)));
    }
}
