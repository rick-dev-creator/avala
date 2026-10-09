using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Verifying;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Tests.Verifying;

public sealed class DeclaredChecksTests
{
    private const string Repository = "/repositories/shop";
    private const string ChecksFile = ".avala/checks.json";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositorysCurrentChecksAreListedInOrderWithTheirCommandLineAndTimeoutAsync()
    {
        var committed = new CommittedFiles().With(
            Repository,
            ChecksFile,
            """{ "checks": [ { "name": "build", "command": "dotnet", "arguments": ["build"], "timeoutSeconds": 90 }, { "name": "grep", "command": "git", "arguments": ["grep", "a b"] } ] }""");

        var checks = await new DeclaredChecks(committed).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal((ChecksFileStatus.Applied, Option<FileOrigin>.Some(CommittedFiles.Origin())), (checks.File, checks.Origin));
        Assert.Equal(
            [new CheckDeclared("build", "dotnet build", TimeSpan.FromSeconds(90)), new CheckDeclared("grep", "git grep \"a b\"", TimeSpan.FromMinutes(10))],
            checks.Checks);
    }

    [Theory]
    [InlineData(null, nameof(ChecksFileStatus.Absent), true)]
    [InlineData("""{ "checks": [ { "arguments": ["build"] } ] }""", nameof(ChecksFileStatus.Rejected), true)]
    [InlineData("unreadable", nameof(ChecksFileStatus.Rejected), false)]
    public async Task ARepositoryWithoutValidChecksListsNoneAndSaysWhyAsync(string? declaration, string status, bool hasOrigin)
    {
        var committed = declaration switch
        {
            null => new CommittedFiles().Workspace(Repository),
            "unreadable" => new CommittedFiles().Failing(Repository, WorkspaceFailure.NotAGitRepository),
            _ => new CommittedFiles().With(Repository, ChecksFile, declaration),
        };

        var checks = await new DeclaredChecks(committed).OfRepositoryAsync(Repository, Cancellation);

        Assert.Equal((Enum.Parse<ChecksFileStatus>(status), 0, hasOrigin), (checks.File, checks.Checks.Count, checks.Origin.IsSome));
    }
}
