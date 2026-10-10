using Avala.Resources.Disks;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Resources.Tests.Disks;

public sealed class FolderSizesTests : IAsyncDisposable
{
    private readonly TemporaryFolder data = new();
    private readonly TemporaryFolder elsewhere = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDataFolderWeighsEveryFileInItAndItsSubfoldersAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(data.Path, "top.txt"), new string('a', 100), Cancellation);
        Directory.CreateDirectory(Path.Combine(data.Path, "deep", "deeper"));
        await File.WriteAllTextAsync(Path.Combine(data.Path, "deep", "deeper", "inner.txt"), new string('b', 23), Cancellation);

        var size = await new FolderSizes(new AvalaPaths(data.Path)).DataFolderAsync(Cancellation);

        Assert.Equal(123, size);
    }

    [Fact]
    public async Task ALinkedFolderIsNotWeighedForItsFilesLiveElsewhereAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(data.Path, "own.txt"), new string('a', 10), Cancellation);
        await File.WriteAllTextAsync(Path.Combine(elsewhere.Path, "outside.txt"), new string('b', 1000), Cancellation);
        Directory.CreateSymbolicLink(Path.Combine(data.Path, "linked"), elsewhere.Path);

        var size = await new FolderSizes(new AvalaPaths(data.Path)).SizeAsync(data.Path, Cancellation);

        Assert.Equal(10, size);
    }

    [Fact]
    public async Task AFolderThatDoesNotExistWeighsNothingAsync()
    {
        var size = await new FolderSizes(new AvalaPaths(data.Path)).SizeAsync(Path.Combine(data.Path, "missing"), Cancellation);

        Assert.Equal(0, size);
    }

    public async ValueTask DisposeAsync()
    {
        await data.DisposeAsync();
        await elsewhere.DisposeAsync();
    }
}
