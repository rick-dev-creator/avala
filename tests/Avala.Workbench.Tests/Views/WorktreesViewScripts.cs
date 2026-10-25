using Avala.Testing.UI;
using Avala.Workbench.Resources;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class WorktreesViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePageShowsTheSpaceItsRowsAndItsDaysAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreesViewModel(), ThemeVariant.Dark, 900, 1400);

            Assert.Equal(("Worktrees", "3.4 GB in 4 worktrees"), (view.TextOf("Title"), view.TextOf("Space")));
            Assert.Equal((2, 1, 2, 2, 1), (view.Find<ItemsControl>("Largest").ItemCount, view.Find<ItemsControl>("Upcoming").ItemCount, view.Find<ItemsControl>("Days").ItemCount, view.Find<ItemsControl>("Recovered").ItemCount, view.Find<ItemsControl>("History").ItemCount));
            Assert.False(view.Find<Border>("Shortage").IsVisible);
        }, Cancellation);

    [Fact]
    public Task AShortageIsShownAboveTheRowsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreesViewModel { Shortage = "Avala freed every worktree it could and still needs space." }, ThemeVariant.Light, 900, 1400);

            Assert.True(view.Find<Border>("Shortage").IsVisible);
        }, Cancellation);
}
