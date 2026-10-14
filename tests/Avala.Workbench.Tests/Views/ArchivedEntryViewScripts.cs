using Avala.Testing.UI;
using Avala.Workbench.Archive;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ArchivedEntryViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheEntryShowsHowManyAreArchivedAndOpensTheArchivedPageAsync() =>
        ui.RunAsync(() =>
        {
            var opened = 0;
            var view = Screen.Show(new DesignArchivedEntryViewModel { OpenCommand = new RelayCommand(() => opened++) });

            view.Click("Open");

            Assert.Equal(("14", 1), (view.TextOf("Count"), opened));
        }, Cancellation);

    [Fact]
    public Task WithNothingArchivedTheEntryIsHiddenAsync() =>
        ui.RunAsync(() => Assert.False(Screen.Show(new DesignArchivedEntryViewModel { HasArchived = false }).Shows("Open")), Cancellation);
}
