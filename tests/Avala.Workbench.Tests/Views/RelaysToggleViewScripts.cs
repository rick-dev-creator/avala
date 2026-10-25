using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class RelaysToggleViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheGlyphShowsSelectedWhileThePaneIsOpenAndHidesForAJobThatNeverRelayedAsync() =>
        ui.RunAsync(() =>
        {
            var open = Screen.Show(new DesignRelaysToggleViewModel());
            var hidden = Screen.Show(new DesignRelaysToggleViewModel { IsShown = false, IsOpen = false });

            Assert.Contains("selected", open.Find<Button>("Relays").Classes);
            Assert.False(hidden.Find<Button>("Relays").IsVisible);
        }, TestContext.Current.CancellationToken);
}
