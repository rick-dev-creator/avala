using Avala.Testing;
using Avala.Workspaces.BaseFiles;
using Avala.Workspaces.Tests.Provisioning;

namespace Avala.Workspaces.Tests.BaseFiles;

public sealed class BaseFileReaderTests
{
    private const string Rules = ".avala/permissions.json";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadingTheSameFileOfTheSameCommitAgainAsksGitForItOnceAsync(bool committed)
    {
        var git = new FakeGit();

        if (committed)
        {
            git.Committed[Rules] = ("4b825dc642cb6eb9a060e54bf8d69288fbee4904", "{ \"rules\": [] }");
        }

        var reader = new BaseFileReader(git, new InMemoryWorkspaceStore());

        var first = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));
        var second = Outcomes.Succeeds(await reader.ReadCurrentAsync(git.Repository, Rules, Cancellation));

        Assert.Equal(first, second);
        Assert.Equal((1, committed ? 1 : 0), (git.CommittedLookups, git.BlobReads));
    }
}
