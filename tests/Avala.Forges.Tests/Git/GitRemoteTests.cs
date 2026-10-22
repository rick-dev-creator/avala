using Avala.Forges.Git;
using Avala.Forges.Tests.Transport;
using Avala.Sdk.Processes;

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

    private static async Task<bool> HeldAsync(string subject) =>
        await new GitRemote(new ScriptedRunner(new ProcessOutcome(0, $"{Commit} {subject}\n", string.Empty))).HeldAsync("/work/fix", Commit, Cancellation);
}
