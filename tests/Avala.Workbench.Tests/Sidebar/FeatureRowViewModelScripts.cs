using Avala.Documentation.Contracts;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class FeatureRowViewModelScripts
{
    [Fact]
    public void ARowNamesTheFeatureAndItsVersionsAndOpensIt()
    {
        var opened = 0;
        var row = new FeatureRowViewModel(new RepositoryFeature(new Feature("payouts", "Payout scheduler"), 1), () => opened++);

        row.OpenCommand.Execute(null);

        Assert.Equal(("Payout scheduler", "1 version", 1), (row.Title, row.Fact, opened));
    }
}
