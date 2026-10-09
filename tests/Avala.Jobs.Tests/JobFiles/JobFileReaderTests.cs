using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.JobFiles;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Jobs.Tests.JobFiles;

public sealed class JobFileReaderTests
{
    private const string Worktree = "/worktrees/1";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{ "connection": "work", "approval": "merge" }""", "work", "merge")]
    [InlineData("""{ "approval": "keep" }""", "", "keep")]
    [InlineData("""{ "connection": "auto", "approval": "keep" }""", "", "keep")]
    [InlineData("{}", "", "")]
    [InlineData("""{ "approval": "merge", "autopilot": { "approve": "cleanEvidence" } }""", "", "merge")]
    [InlineData("""{ "approval": "merge", "delegation": { "connections": ["work", "personal"], "maxDepth": 2 } }""", "", "merge")]
    public async Task TheJobFileOfTheBaseCommitNamesTheRepositorysDefaultConnectionAndApprovalAsync(string text, string connection, string approval)
    {
        var committed = new CommittedFiles().With(Worktree, JobFileReader.JobFile, text, editedInWorktree: true);
        var reader = Reader(committed);

        var named = Outcomes.Succeeds(await reader.ConnectionAsync(Worktree, Cancellation));
        var strategy = Outcomes.Succeeds(await reader.ApprovalAsync(Worktree, Cancellation));

        Assert.Equal((connection, approval), (named.Match(name => name.Value, () => string.Empty), strategy.Match(name => name, () => string.Empty)));
        Assert.Equal([(Worktree, JobFileReader.JobFile), (Worktree, JobFileReader.JobFile)], committed.Reads);
    }

    [Fact]
    public async Task ABaseCommitWithoutAJobFileHasNoDefaultConnectionNorApprovalAsync()
    {
        var reader = Reader(new CommittedFiles().Workspace(Worktree));

        Assert.Equal(Option<ConnectionName>.None, Outcomes.Succeeds(await reader.ConnectionAsync(Worktree, Cancellation)));
        Assert.Equal(Option<string>.None, Outcomes.Succeeds(await reader.ApprovalAsync(Worktree, Cancellation)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "connection": 1 }""")]
    [InlineData("""{ "connection": "" }""")]
    [InlineData("""{ "connection": "a", "connection": "b" }""")]
    [InlineData("""{ "connection": "work", "model": "large" }""")]
    [InlineData("""{ "approval": 1 }""")]
    [InlineData("""{ "approval": "" }""")]
    [InlineData("""{ "approval": { "name": "merge" } }""")]
    [InlineData("""{ "autopilot": "on" }""")]
    [InlineData("""{ "autopilot": { "approve": { "when": "clean" } } }""")]
    [InlineData("""{ "autopilot": { "approve": ["cleanEvidence"] } }""")]
    [InlineData("""{ "delegation": ["work"] }""")]
    [InlineData("""{ "delegation": { "connections": [["work"]] } }""")]
    [InlineData("""{ "delegation": { "routing": { "name": "roundRobin" } } }""")]
    public async Task AnInvalidJobFileMakesTheDefaultConnectionUnusableAndTheApprovalInvalidAsync(string text)
    {
        var reader = Reader(new CommittedFiles().With(Worktree, JobFileReader.JobFile, text));

        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await reader.ConnectionAsync(Worktree, Cancellation)));
        Assert.Equal(JobRejection.InvalidJobFile, Outcomes.FailsWith(await reader.ApprovalAsync(Worktree, Cancellation)));
    }

    [Fact]
    public async Task AJobFileOverTheSizeLimitOrABaseCommitThatCannotBeReadIsRejectedAsync()
    {
        var large = Reader(new CommittedFiles().With(Worktree, JobFileReader.JobFile, new string(' ', JobFileReader.MaximumBytes + 1)));
        var failing = Reader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed));

        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await large.ConnectionAsync(Worktree, Cancellation)));
        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await failing.ConnectionAsync(Worktree, Cancellation)));
        Assert.Equal(JobRejection.InvalidJobFile, Outcomes.FailsWith(await large.ApprovalAsync(Worktree, Cancellation)));
        Assert.Equal(JobRejection.InvalidJobFile, Outcomes.FailsWith(await failing.ApprovalAsync(Worktree, Cancellation)));
    }

    [Theory]
    [InlineData("""{ "connection": "work" }""", "work", "")]
    [InlineData("""{ "connection": "auto" }""", "", "")]
    [InlineData("""{ "connection": 1 }""", "", "InvalidJobFile")]
    public async Task TheRepositorysCurrentCommitTellsWhichConnectionAJobSubmittedNowWouldPreferAsync(string text, string connection, string rejection)
    {
        var current = await Reader(new CommittedFiles().With("/repositories/shop", JobFileReader.JobFile, text)).CurrentConnectionAsync("/repositories/shop", Cancellation);

        Assert.Equal(
            (connection, rejection),
            current.Match(found => (found.Match(name => name.Value, () => string.Empty), string.Empty), error => (string.Empty, error.ToString())));
    }

    [Fact]
    public async Task APathThatIsNoRepositoryYetPrefersNoConnectionAsync()
    {
        var current = await Reader(new CommittedFiles().Failing("/repositories/sho", WorkspaceFailure.GitFailed)).CurrentConnectionAsync("/repositories/sho", Cancellation);

        Assert.True(Outcomes.Succeeds(current).IsNone);
    }

    private static JobFileReader Reader(CommittedFiles files) => new(files, NullLogger<JobFileReader>.Instance);
}
