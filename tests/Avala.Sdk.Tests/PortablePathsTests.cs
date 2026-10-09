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
    public void AnEmptyFolderMarksNothing() =>
        Assert.Equal("C:\\work\\a.md", "C:\\work\\a.md".Marking(string.Empty, "${workingDirectory}"));
}
