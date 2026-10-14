using Avala.Workbench.Linking;

namespace Avala.Workbench.Tests.Conversation;

public sealed class JobReferencesScripts
{
    [Theory]
    [InlineData("git@github.com:acme/dashboard.git", "avala-ref:commit/01a13a5", "https://github.com/acme/dashboard/commit/01a13a5")]
    [InlineData("https://git.rick-lab.dev/rick-dev/avala-method.git", "avala-ref:issue/12", "https://git.rick-lab.dev/rick-dev/avala-method/issues/12")]
    public async Task ACommitOrAnIssueOpensOnTheRepositorysForgeAsync(string origin, string link, string page)
    {
        var worktree = new Worktree();
        worktree.Remotes.Origin = origin;
        var message = worktree.Message("It landed.");

        await message.OpenLinkCommand.ExecuteAsync(link);

        Assert.Equal([new Uri(page)], worktree.Opener.Opened);
        Assert.Equal(string.Empty, message.LinkNotice);
    }

    [Fact]
    public async Task ACommitInARepositoryWithoutAWebAddressSaysSoAsync()
    {
        var worktree = new Worktree();
        var message = worktree.Message("It landed in 01a13a5.");

        await message.OpenLinkCommand.ExecuteAsync("avala-ref:commit/01a13a5");

        Assert.Empty(worktree.Opener.Opened);
        Assert.Equal("This repository has no web address to open it on.", message.LinkNotice);
    }

    [Theory]
    [InlineData("git@github.com:acme/dashboard.git", "https://github.com/acme/dashboard")]
    [InlineData("https://token@github.com/acme/dashboard.git", "https://github.com/acme/dashboard")]
    [InlineData("ssh://git@git.rick-lab.dev:2222/rick-dev/avala.git", "https://git.rick-lab.dev/rick-dev/avala")]
    [InlineData("https://gitea.example.com/team/app/", "https://gitea.example.com/team/app")]
    public void ARemoteNamesItsRepositorysWebAddress(string remote, string web) =>
        Assert.Equal(new Uri(web), JobReferences.Web(remote).Match(found => found, () => new Uri("https://none")));

    [Theory]
    [InlineData("/home/ana/repos/app")]
    [InlineData("file:///home/ana/repos/app")]
    [InlineData("https://github.com/acme")]
    public void ARemoteWithoutARepositoryPageHasNoWebAddress(string remote) =>
        Assert.True(JobReferences.Web(remote).IsNone);
}
