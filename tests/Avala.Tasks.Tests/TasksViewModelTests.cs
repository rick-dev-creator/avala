namespace Avala.Tasks.Tests;

public sealed class TasksViewModelTests
{
    [Fact]
    public void AddsTheTrimmedDraft()
    {
        var tasks = new TasksViewModel { Draft = "  Add GitHub login  " };

        tasks.AddCommand.Execute(null);

        Assert.Equal(["Add GitHub login"], tasks.Items);
    }

    [Fact]
    public void ClearsTheDraftAfterAdding()
    {
        var tasks = new TasksViewModel { Draft = "Paginate orders" };

        tasks.AddCommand.Execute(null);

        Assert.Empty(tasks.Draft);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CannotAddABlankDraft(string draft)
    {
        var tasks = new TasksViewModel { Draft = draft };

        Assert.False(tasks.AddCommand.CanExecute(null));
    }

    [Fact]
    public void EnablesAddingOnceTheDraftHasText()
    {
        var tasks = new TasksViewModel();

        tasks.Draft = "Export orders";

        Assert.True(tasks.AddCommand.CanExecute(null));
    }
}
