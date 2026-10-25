using Avala.Testing.UI;
using Avala.Workbench.Resources;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class WorktreeDayViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ADayShowsItsPeakAndItsShareAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeDayViewModel());

            Assert.Equal(("Sun 25 Oct · today", "3.4 GB", 0.6), (view.TextOf("Day"), view.TextOf("Peak"), view.Find<ProgressBar>("Share").Value));
        }, TestContext.Current.CancellationToken);
}
