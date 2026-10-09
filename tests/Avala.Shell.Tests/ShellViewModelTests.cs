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

    [Fact]
    public void ActivatesTheSelectedPageAndDeactivatesTheOneItLeaves()
    {
        var first = new ActivePage("Jobs");
        var second = new ActivePage("Usage");
        var shell = new ShellViewModel([first, second]);

        shell.SelectedPage = second;

        Assert.Equal(["activated", "deactivated"], first.Calls);
        Assert.Equal(["activated"], second.Calls);
    }

    private sealed record StubPage(string Title) : IPage;

    private sealed class ActivePage(string title) : IPage, IActivatable
    {
        public string Title { get; } = title;

        public List<string> Calls { get; } = [];

        public void Activate() => Calls.Add("activated");

        public void Deactivate() => Calls.Add("deactivated");
    }
}
