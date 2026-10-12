using Avala.Components.UI.Folders;
using Avala.Testing;

namespace Avala.Components.UI.Tests;

public sealed class StartingFoldersTests
{
    [Fact]
    public void ATypedFolderThatDoesNotExistYetStartsAtItsNearestExistingParent()
    {
        using var home = new TemporaryFolder();
        var projects = Directory.CreateDirectory(Path.Combine(home.Path, "projects")).FullName;

        Assert.Equal(projects, StartingFolders.Near($"  {Path.Combine(projects, "avala", "src")}  ", home.Path));
    }

    [Fact]
    public void AFolderTypedUnderTildeIsFoundInTheHomeFolder()
    {
        using var home = new TemporaryFolder();
        var projects = Directory.CreateDirectory(Path.Combine(home.Path, "projects")).FullName;

        Assert.Equal(Path.GetFullPath(projects), Path.GetFullPath(StartingFolders.Near("~/projects/avala", home.Path)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("projects/avala")]
    public void AnEmptyOrRelativeFolderStartsAtHome(string typed) =>
        Assert.Equal("home", StartingFolders.Near(typed, "home"));
}
