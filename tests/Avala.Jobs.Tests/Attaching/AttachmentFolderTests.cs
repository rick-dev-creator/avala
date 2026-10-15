using Avala.Jobs.Attaching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Attaching;

public sealed class AttachmentFolderTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("screen.png", "image/png")]
    [InlineData("spec.PDF", "application/pdf")]
    [InlineData("build.log", "text/plain")]
    [InlineData("Program.cs", "text/plain")]
    [InlineData("archive.zip", "application/octet-stream")]
    public async Task AFileIsKeptAsACopyInAvalasDataFolderWithTheTypeItsNameSaysAsync(string name, string mediaType)
    {
        using var data = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        var original = Path.Combine(elsewhere.Path, name);
        await File.WriteAllTextAsync(original, "content", Cancellation);

        var kept = Outcomes.Succeeds(await new AttachmentFolder(new AvalaPaths(data.Path)).KeepFileAsync(original, Cancellation));
        File.Delete(original);

        Assert.Equal((name, mediaType), (kept.Name, kept.MediaType));
        Assert.StartsWith(Path.Combine(data.Path, AttachmentFolder.Folder), kept.Path, StringComparison.Ordinal);
        Assert.Equal("content", await File.ReadAllTextAsync(kept.Path, Cancellation));
    }

    [Fact]
    public async Task PastedContentIsKeptUnderItsNameAsync()
    {
        using var data = new TemporaryFolder();
        using var pasted = new MemoryStream([137, 80, 78, 71]);

        var kept = Outcomes.Succeeds(await new AttachmentFolder(new AvalaPaths(data.Path)).KeepAsync("pasted.png", pasted, Cancellation));

        Assert.Equal(("pasted.png", "image/png"), (kept.Name, kept.MediaType));
        Assert.Equal([137, 80, 78, 71], await File.ReadAllBytesAsync(kept.Path, Cancellation));
    }

    [Fact]
    public async Task AFileThatIsTooLargeOrUnreadableIsRefusedAsync()
    {
        using var data = new TemporaryFolder();
        var folder = new AttachmentFolder(new AvalaPaths(data.Path));
        using var huge = new MemoryStream(new byte[AttachmentFolder.MaximumBytes + 1]);

        Assert.Equal(AttachmentRefusal.TooLarge, Outcomes.FailsWith(await folder.KeepAsync("dump.bin", huge, Cancellation)));
        Assert.Equal(AttachmentRefusal.Unreadable, Outcomes.FailsWith(await folder.KeepFileAsync(Path.Combine(data.Path, "missing.txt"), Cancellation)));
        Assert.False(Directory.Exists(Path.Combine(data.Path, AttachmentFolder.Folder)) && Directory.EnumerateFiles(Path.Combine(data.Path, AttachmentFolder.Folder), "*", SearchOption.AllDirectories).Any());
    }
}
