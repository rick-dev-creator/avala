using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia.Controls.Shapes;

namespace Avala.Workbench.Tests.Views;

public sealed class RelayLegViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ALegShowsItsConnectionWhatHappenedAndWhenWithItsStopColouredByStateAsync() =>
        ui.RunAsync(() =>
        {
            var ran = Screen.Show(new DesignRelayLegViewModel());
            var waiting = Screen.Show(new DesignRelayLegViewModel { Title = "Waiting for claude-work", Detail = "Compacts, then resumes at its reset", When = "17:44", IsCurrent = true, IsWaiting = true });

            Assert.Equal(("claude-team", "Ran 2h 38m · wound down at 90%", "15:04"), (ran.TextOf("Title"), ran.TextOf("Detail"), ran.TextOf("When")));
            Assert.Empty(ran.Find<Ellipse>("Stop").Classes);
            Assert.Equal(["current", "waiting"], waiting.Find<Ellipse>("Stop").Classes.Order());
        }, TestContext.Current.CancellationToken);
}
