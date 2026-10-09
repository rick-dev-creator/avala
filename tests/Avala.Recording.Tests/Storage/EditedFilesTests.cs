using Avala.Recording.Storage;
using Avala.Testing;

namespace Avala.Recording.Tests.Storage;

public sealed class EditedFilesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStampListsEveryFileOfTheFolderWithItsLengthExceptThoseOfGitAsync()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "docs"));
        Directory.CreateDirectory(Path.Combine(folder.Path, ".git"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "a.txt"), "abc", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "docs", "b.md"), "hello", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder.Path, ".git", "HEAD"), "ref", Cancellation);

        var stamps = await new EditedFiles().StampAsync(folder.Path, Cancellation);

        Assert.Equal(
            [(Path.Combine(folder.Path, "a.txt"), 3L), (Path.Combine(folder.Path, "docs", "b.md"), 5L)],
            stamps.Files.Select(file => (file.Key, file.Value.Length)).Order());
    }

    [Fact]
    public async Task AFolderThatDoesNotExistStampsNoFilesAsync()
    {
        using var folder = new TemporaryFolder();

        var stamps = await new EditedFiles().StampAsync(Path.Combine(folder.Path, "missing"), Cancellation);

        Assert.Empty(stamps.Files);
    }
}
