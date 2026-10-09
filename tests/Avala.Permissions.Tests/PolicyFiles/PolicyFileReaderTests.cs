using Avala.Permissions.Contracts;
using Avala.Permissions.PolicyFiles;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests.PolicyFiles;

public sealed class PolicyFileReaderTests
{
    private const string Worktree = "/worktrees/1";
    private const string PolicyFile = ".avala/permissions.json";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABaseCommitWithoutAPolicyFileHasNoRepositoryRulesAsync()
    {
        var file = await new PolicyFileReader(new CommittedFiles().Workspace(Worktree)).ReadAsync(Worktree, Cancellation);

        Assert.True(Outcomes.Succeeds(file.Rules).IsNone);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin()), file.Origin);
    }

    [Fact]
    public async Task ThePolicyFileOfTheBaseCommitIsParsedWithItsOriginAsync()
    {
        var committed = new CommittedFiles().With(
            Worktree,
            PolicyFile,
            """{ "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test", "answer": "allow" } ] }""",
            editedInWorktree: true);

        var file = await new PolicyFileReader(committed).ReadAsync(Worktree, Cancellation);

        Assert.Equal("tests", Assert.Single(Outcomes.Present(Outcomes.Succeeds(file.Rules))).Name);
        Assert.Equal(Option<FileOrigin>.Some(CommittedFiles.Origin(editedInWorktree: true)), file.Origin);
        Assert.Equal([(Worktree, PolicyFile)], committed.Reads);
    }

    [Fact]
    public async Task APolicyFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        var committed = new CommittedFiles().With(Worktree, PolicyFile, $$"""{ "rules": [], "padding": "{{new string(' ', PolicyFileReader.MaximumBytes)}}" }""");

        Assert.Equal(PolicyError.TooLarge, Outcomes.FailsWith((await new PolicyFileReader(committed).ReadAsync(Worktree, Cancellation)).Rules));
    }

    [Fact]
    public async Task AFolderThatIsNoJobWorkspaceHasNoRepositoryRulesAndNoOriginAsync()
    {
        var file = await new PolicyFileReader(new CommittedFiles()).ReadAsync("/elsewhere", Cancellation);

        Assert.True(Outcomes.Succeeds(file.Rules).IsNone);
        Assert.True(file.Origin.IsNone);
    }

    [Fact]
    public async Task ABaseCommitThatCannotBeReadIsUnreadableAsync()
    {
        var file = await new PolicyFileReader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed)).ReadAsync(Worktree, Cancellation);

        Assert.Equal(PolicyError.Unreadable, Outcomes.FailsWith(file.Rules));
    }
}
