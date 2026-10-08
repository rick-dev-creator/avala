using Avala.Sdk;

namespace Avala.Shell.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void ExposesEveryContributedPage()
    {
        var pages = new[] { new StubPage("Tasks"), new StubPage("Review") };

        var shell = new ShellViewModel(pages);

        Assert.Equal(pages, shell.Pages);
    }

    [Fact]
    public void SelectsTheFirstPageByDefault()
    {
        var first = new StubPage("Tasks");

        var shell = new ShellViewModel([first, new StubPage("Review")]);

        Assert.Same(first, shell.SelectedPage);
    }

    [Fact]
    public void SelectsNothingWhenNoPluginContributesPages()
    {
        var shell = new ShellViewModel([]);

        Assert.Null(shell.SelectedPage);
    }

    private sealed record StubPage(string Title) : IPage;
}
