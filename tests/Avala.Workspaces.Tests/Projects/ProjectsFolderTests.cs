using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Projects;

namespace Avala.Workspaces.Tests.Projects;

public sealed class ProjectsFolderTests : IAsyncDisposable
{
    private readonly TemporaryFolder home = new();
    private readonly TemporaryFolder data = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ProjectsSettings Settings => new(home.Path, Path.Combine(data.Path, ProjectsSettings.FileName));

    [Theory]
    [InlineData(new[] { "src", "code" }, "code")]
    [InlineData(new[] { "dev", "Projects", "code" }, "Projects")]
    [InlineData(new[] { "source/repos", "Downloads" }, "source/repos")]
    [InlineData(new[] { "Downloads" }, "")]
    public async Task TheFirstUsualProjectsFolderThatExistsIsSuggestedAndTheHomeFolderOtherwiseAsync(string[] existing, string expected)
    {
        foreach (var folder in existing)
        {
            Directory.CreateDirectory(Path.Combine(home.Path, folder));
        }

        await using var projects = new ProjectsFolders(Settings);

        var read = await projects.ReadAsync(Cancellation);

        Assert.Equal((Path.Combine(home.Path, expected.Replace('/', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar), Option<string>.None), (read.Suggested.TrimEnd(Path.DirectorySeparatorChar), read.Chosen));
    }

    [Fact]
    public async Task AChosenProjectsFolderIsKeptAcrossARestartAsync()
    {
        var chosen = Directory.CreateDirectory(Path.Combine(home.Path, "work")).FullName;
        await using (var projects = new ProjectsFolders(Settings))
        {
            _ = Outcomes.Succeeds(await projects.ChooseAsync(chosen, Cancellation));
        }

        await using var restarted = new ProjectsFolders(Settings);

        Assert.Equal(chosen, (await restarted.ReadAsync(Cancellation)).Folder);
    }

    [Theory]
    [InlineData("work", WorkspaceFailure.RelativeFolder)]
    [InlineData("{home}/missing", WorkspaceFailure.FolderMissing)]
    public async Task ARelativeOrMissingFolderIsRefusedAndNothingIsKeptAsync(string folder, WorkspaceFailure expected)
    {
        await using var projects = new ProjectsFolders(Settings);

        Assert.Equal(expected, Outcomes.FailsWith(await projects.ChooseAsync(folder.Replace("{home}", home.Path, StringComparison.Ordinal), Cancellation)));
        Assert.Equal((Option<string>.None, false), ((await projects.ReadAsync(Cancellation)).Chosen, File.Exists(Settings.File)));
    }

    public async ValueTask DisposeAsync()
    {
        await home.DisposeAsync();
        await data.DisposeAsync();
    }
}
