using Avala.Permissions.Governance;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Permissions.Tests.Governance;

public sealed class SessionTermsTests
{
    [Theory]
    [InlineData("/work/shop", "/data", true)]
    [InlineData("/tmp/shop", "/data", false)]
    [InlineData("/tmp", "/data", false)]
    [InlineData("/work/shop", "/tmp/avala", false)]
    public void ATemporaryFolderIsTheJobsOwnOnlyWhenItHoldsNeitherItsWorktreeNorAvalasData(string worktree, string data, bool writable)
    {
        var paths = new AvalaPaths(Path.GetFullPath(data));
        var terms = new SessionTerms(new NoPolicyFiles(), [], paths, [Path.GetFullPath("/tmp")]);

        Assert.Equal(
            [ScratchFolders.Of(paths, Path.GetFullPath(worktree)), .. writable ? [Path.GetFullPath("/tmp")] : Array.Empty<string>()],
            terms.WritableOf(Path.GetFullPath(worktree)));
    }
}
