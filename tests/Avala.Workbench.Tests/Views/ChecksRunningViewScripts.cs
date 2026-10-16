using Avala.Testing.UI;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Views;

public sealed class ChecksRunningViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARunningCheckShowsItsChipInTheHeaderAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignChecksRunningViewModel());

            Assert.Equal(("Checks running · 2 of 3", true), (view.TextOf("Text"), view.Shows("Chip")));
        }, Cancellation);

    [Fact]
    public Task NoRunningCheckShowsNoChipAsync() =>
        ui.RunAsync(() => Assert.False(Screen.Show(new DesignChecksRunningViewModel { IsShown = false }).Shows("Chip")), Cancellation);
}
