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
    [InlineData("""{ "connection": "work" }""", "work")]
    [InlineData("{}", "")]
    public async Task TheJobFileOfTheBaseCommitNamesTheRepositorysDefaultConnectionAsync(string text, string expected)
    {
        var committed = new CommittedFiles().With(Worktree, JobFileReader.JobFile, text, editedInWorktree: true);

        var connection = Outcomes.Succeeds(await Reader(committed).ConnectionAsync(Worktree, Cancellation));

        Assert.Equal(expected, connection.Match(name => name.Value, () => string.Empty));
        Assert.Equal([(Worktree, JobFileReader.JobFile)], committed.Reads);
    }

    [Fact]
    public async Task ABaseCommitWithoutAJobFileHasNoDefaultConnectionAsync()
    {
        var connection = await Reader(new CommittedFiles().Workspace(Worktree)).ConnectionAsync(Worktree, Cancellation);

        Assert.Equal(Option<ConnectionName>.None, Outcomes.Succeeds(connection));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "connection": 1 }""")]
    [InlineData("""{ "connection": "" }""")]
    [InlineData("""{ "connection": "a", "connection": "b" }""")]
    [InlineData("""{ "connection": "work", "model": "large" }""")]
    public async Task AnInvalidJobFileMakesTheDefaultConnectionUnusableAsync(string text)
    {
        var connection = await Reader(new CommittedFiles().With(Worktree, JobFileReader.JobFile, text)).ConnectionAsync(Worktree, Cancellation);

        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(connection));
    }

    [Fact]
    public async Task AJobFileOverTheSizeLimitOrABaseCommitThatCannotBeReadIsUnusableAsync()
    {
        var large = new CommittedFiles().With(Worktree, JobFileReader.JobFile, new string(' ', JobFileReader.MaximumBytes + 1));
        var failing = new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed);

        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await Reader(large).ConnectionAsync(Worktree, Cancellation)));
        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await Reader(failing).ConnectionAsync(Worktree, Cancellation)));
    }

    private static JobFileReader Reader(CommittedFiles files) => new(files, NullLogger<JobFileReader>.Instance);
}
