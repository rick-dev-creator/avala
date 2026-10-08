using Avala.Testing;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Tests.Domain;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("avala/0192f1a2")]
    [InlineData("feature/login-github")]
    public void AcceptsValidBranchNames(string name) =>
        Assert.Equal(name, Outcomes.Succeeds(BranchName.Create(name)).Value);

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-starts-with-dash")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("ends.")]
    [InlineData("branch.lock")]
    [InlineData("a..b")]
    [InlineData("a@{b")]
    [InlineData("a~b")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    public void RejectsInvalidBranchNames(string name) =>
        Assert.Equal(WorkspaceError.InvalidBranchName, Outcomes.FailsWith(BranchName.Create(name)));

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef01234567\n")]
    public void AcceptsShaOneAndShaTwoFiftySixCommits(string sha) =>
        Assert.Equal(sha.Trim(), Outcomes.Succeeds(CommitSha.Create(sha)).Value);

    [Theory]
    [InlineData("")]
    [InlineData("0123456")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData("z123456789abcdef0123456789abcdef01234567")]
    public void RejectsMalformedCommits(string sha) =>
        Assert.Equal(WorkspaceError.InvalidCommit, Outcomes.FailsWith(CommitSha.Create(sha)));

    [Theory]
    [InlineData("", "/worktrees/1")]
    [InlineData("/repos/shop", " ")]
    public void ALocationNeedsARepositoryAndAPath(string repository, string path) =>
        Assert.Equal(WorkspaceError.EmptyLocation, Outcomes.FailsWith(WorkspaceLocation.Create(repository, path)));
}
