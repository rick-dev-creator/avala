using System.Text.Json;
using Avala.Permissions.Answering;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Answering;

public sealed class ReadableFoldersTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheProjectsFolderIsReadableUntilReadableJsonNamesTheFoldersAndAMalformedFileNamesNoneAsync()
    {
        using var data = new TemporaryFolder();
        var projects = Path.Combine(data.Path, "Projects");
        var folders = new ReadableFolders(new AvalaPaths(data.Path), new FixedProjectsFolder(projects));
        var file = Path.Combine(data.Path, ReadableFolders.FileName);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var repos = JsonSerializer.Serialize(Path.Combine(data.Path, "repos"));

        var absent = await folders.FoldersAsync(Cancellation);
        await File.WriteAllTextAsync(file, $$"""{ "folders": ["~/code", {{repos}}, "relative"] }""", Cancellation);
        var named = await folders.FoldersAsync(Cancellation);
        await File.WriteAllTextAsync(file, $$"""{ "folders": [{{repos}}], "extra": true }""", Cancellation);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(1));
        var malformed = await folders.FoldersAsync(Cancellation);

        Assert.Equal([projects], absent);
        Assert.Equal([Path.Join(home, "/code"), Path.Combine(data.Path, "repos")], named);
        Assert.Empty(malformed);
    }
}
