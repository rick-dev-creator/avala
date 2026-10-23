using Avala.Testing;
using Avala.Workspaces.BaseFiles;
using Avala.Workspaces.Tests.Provisioning;

namespace Avala.Workspaces.Tests.BaseFiles;

public sealed class BaseFileReaderTests
{
    private const string Rules = ".avala/permissions.json";

    private const string Blob = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadingTheSameFileOfTheSameCommitAgainAsksGitForItOnceAsync(bool committed)
    {
        var git = new FakeGit();

        if (committed)
        {
            git.Committed[Rules] = (Blob, "{ \"rules\": [] }");
        }

        var reader = new BaseFileReader(git, new InMemoryWorkspaceStore());

        var first = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));
        var second = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));

        Assert.Equal(first, second);
        Assert.Equal((1, committed ? 1 : 0), (git.CommittedLookups, git.BlobReads));
    }

    [Fact]
    public async Task AFileReadAgainSaysWhetherTheWorktreeEditedItSinceAsync()
    {
        var git = new FakeGit();
        git.Committed[Rules] = (Blob, "{ \"rules\": [] }");
        git.InWorktree[Rules] = Blob;
        var reader = new BaseFileReader(git, new InMemoryWorkspaceStore());
        var untouched = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));

        _ = git.InWorktree.Remove(Rules);
        var edited = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));

        Assert.Equal((false, true), (untouched.Origin.EditedInWorktree, edited.Origin.EditedInWorktree));
    }

    [Fact]
    public async Task AReadGitFailedIsAskedAgainAsync()
    {
        var git = new FakeGit { LookupsFailing = 1 };
        git.Committed[Rules] = (Blob, "{ \"rules\": [] }");
        var reader = new BaseFileReader(git, new InMemoryWorkspaceStore());
        _ = Outcomes.FailsWith(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));

        var read = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));

        Assert.Equal(("{ \"rules\": [] }", 2), (read.Content.Match(content => content, () => string.Empty), git.CommittedLookups));
    }
}
