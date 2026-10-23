using Avala.Forges.Contracts;
using Avala.Forges.Git;
using Avala.Forges.Tests.Transport;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Tests.Git;

public sealed class GitRemoteTests
{
    private const string Commit = "6dcb09b5b57875f334f61aebed695e2e4193db5e";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("commit: Fix the rounding of totals")]
    [InlineData("commit (amend): Fix the rounding of totals")]
    [InlineData("commit: Fast-forward the cache warm-up")]
    [InlineData("revert: Revert \"Fast-forward the cache\"")]
    [InlineData("cherry-pick: Fix the rounding of totals")]
    [InlineData("rebase -i (pick): Fix the rounding of totals")]
    [InlineData("merge fix/rounding: Merge made by the 'ort' strategy.")]
    [InlineData("pull --rebase origin main (pick): Fix the rounding of totals")]
    [InlineData("pull origin main: Merge made by the 'ort' strategy.")]
    public async Task ACommitTheWorktreeMadeItselfIsHeldByItAsync(string subject) =>
        Assert.True(await HeldAsync(subject));

    [Theory]
    [InlineData("pull: Fast-forward")]
    [InlineData("merge origin/main: Fast-forward")]
    [InlineData("pull origin main: fast-forward")]
    [InlineData("reset: moving to HEAD~1")]
    [InlineData("checkout: moving from main to fix/rounding")]
    [InlineData("pull origin main: updating HEAD")]
    public async Task ACommitTheWorktreeOnlyMovedToIsNotHeldByItAsync(string subject) =>
        Assert.False(await HeldAsync(subject));

    [Theory]
    [InlineData("https://github.com/octocat/shop.git", true)]
    [InlineData("", false)]
    public async Task AWorktreesRemoteIsReadThroughItsRepositoryWithoutRunningGitAsync(string url, bool declared)
    {
        var runner = new ScriptedRunner(new ProcessOutcome(0, "https://elsewhere.example/other.git", string.Empty));
        var remotes = new DeclaredRemotes(declared ? url : Option<string>.None);

        var read = await new GitRemote(runner, remotes).UrlAsync("/work/fix", "origin", Cancellation);

        Assert.Equal(declared ? url : nameof(ForgeError.GitFailed), read.Match(found => found, error => error.ToString()));
        Assert.Empty(runner.Requests);
    }

    private static async Task<bool> HeldAsync(string subject) =>
        await new GitRemote(new ScriptedRunner(new ProcessOutcome(0, $"{Commit} {subject}\n", string.Empty)), new DeclaredRemotes(Option<string>.None)).HeldAsync("/work/fix", Commit, Cancellation);

    private sealed class DeclaredRemotes(Option<string> url) : IRemoteRepositories
    {
        public ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken) => ValueTask.FromResult(url);

        public ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Result<string, WorkspaceFailure>>(WorkspaceFailure.GitFailed);

        public ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Result<string, WorkspaceFailure>>(WorkspaceFailure.GitFailed);
    }
}
