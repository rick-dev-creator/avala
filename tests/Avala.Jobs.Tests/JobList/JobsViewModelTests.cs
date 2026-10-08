using Avala.Jobs.JobList;

namespace Avala.Jobs.Tests.JobList;

public sealed class JobsViewModelTests
{
    [Fact]
    public void AddsTheTrimmedDraft()
    {
        var jobs = new JobsViewModel { Draft = "  Add GitHub login  " };

        jobs.AddCommand.Execute(null);

        Assert.Equal(["Add GitHub login"], jobs.Items);
    }

    [Fact]
    public void ClearsTheDraftAfterAdding()
    {
        var jobs = new JobsViewModel { Draft = "Paginate orders" };

        jobs.AddCommand.Execute(null);

        Assert.Empty(jobs.Draft);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CannotAddABlankDraft(string draft)
    {
        var jobs = new JobsViewModel { Draft = draft };

        Assert.False(jobs.AddCommand.CanExecute(null));
    }

    [Fact]
    public void EnablesAddingOnceTheDraftHasText()
    {
        var jobs = new JobsViewModel();

        jobs.Draft = "Export orders";

        Assert.True(jobs.AddCommand.CanExecute(null));
    }
}
