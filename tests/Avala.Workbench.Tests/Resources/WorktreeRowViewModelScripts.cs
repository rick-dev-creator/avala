using Avala.Workbench.Resources;

namespace Avala.Workbench.Tests.Resources;

public sealed class WorktreeRowViewModelScripts
{
    [Fact]
    public void ARowKeepsWhatItShows()
    {
        var row = new WorktreeRowViewModel("Add GitHub login", "82% build output", "1.9 GB");

        Assert.Equal(("Add GitHub login", "82% build output", "1.9 GB"), (row.Title, row.Detail, row.Amount));
    }
}
