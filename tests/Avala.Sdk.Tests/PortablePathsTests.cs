namespace Avala.Sdk.Tests;

public sealed class PortablePathsTests
{
    [Fact]
    public void AMarkedPathIsReplayedWithTheHostSeparators() =>
        Assert.Equal(
            "Write C:\\work\\job-1\\docs\\GREETING.md into \"C:\\work\\job-1\" then read a/b",
            "Write ${workingDirectory}/docs/GREETING.md into \"${workingDirectory}\" then read a/b"
                .Unmarking("${workingDirectory}", "C:\\work\\job-1", '\\'));

    [Fact]
    public void AMarkedPathKeepsForwardSlashesOnAHostThatUsesThem() =>
        Assert.Equal(
            "Write /work/job-1/docs/GREETING.md",
            "Write ${workingDirectory}/docs/GREETING.md".Unmarking("${workingDirectory}", "/work/job-1", '/'));

    [Fact]
    public void APathThroughALinkIsCanonicalAtTheFolderTheLinkLeadsToAndKeepsTheTailThatDoesNotExistYet()
    {
        var scratch = Directory.CreateTempSubdirectory("avala-");

        try
        {
            var real = Directory.CreateDirectory(Path.Combine(scratch.FullName, "real", "repository")).FullName;
            var linked = Directory.CreateSymbolicLink(Path.Combine(scratch.FullName, "linked"), "real").FullName;

            Assert.Equal(
                [Path.Combine(real.Canonical(), "worktrees", "job-1"), real.Canonical()],
                [Path.Combine(linked, "repository", "worktrees", "job-1").Canonical(), (Path.Combine(linked, "repository") + Path.DirectorySeparatorChar).Canonical()]);
            Assert.NotEqual(Path.Combine(linked, "repository"), Path.Combine(linked, "repository").Canonical());
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public void AnEmptyFolderMarksNothing() =>
        Assert.Equal("C:\\work\\a.md", "C:\\work\\a.md".Marking(string.Empty, "${workingDirectory}"));
}
