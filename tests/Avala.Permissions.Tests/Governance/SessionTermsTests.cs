using Avala.Permissions.Governance;
using Avala.Permissions.Links;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

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
        var terms = new SessionTerms(new NoPolicyFiles(), [], new SymbolicLinks(), new WritablePlaces(paths, [Path.GetFullPath("/tmp")]));

        Assert.Equal(
            [ScratchFolders.Of(paths, Path.GetFullPath(worktree)), .. writable ? [Path.GetFullPath("/tmp")] : Array.Empty<string>()],
            terms.WritableOf(Path.GetFullPath(worktree)));
    }

    [Fact]
    public void ATemporaryFolderReachedThroughALinkStillHoldsTheWorktreeItLeadsTo()
    {
        using var real = new TemporaryFolder();
        using var linked = new TemporaryFolder();
        var temporary = Path.Combine(linked.Path, "tmp");
        Directory.CreateSymbolicLink(temporary, real.Path);
        var terms = new SessionTerms(new NoPolicyFiles(), [], new SymbolicLinks(), new WritablePlaces(Option<AvalaPaths>.None, [temporary]));

        Assert.Empty(terms.WritableOf(Path.Combine(real.Path, "shop")));
    }
}
