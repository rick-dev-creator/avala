using Avala.Components.Meters;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class RelayLaneViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheCurrentLaneStandsOutAndCarriesItsConnectionsGaugeAsync() =>
        ui.RunAsync(() =>
        {
            var current = Screen.Show(new DesignRelayLaneViewModel());
            var other = Screen.Show(new DesignRelayLaneViewModel("claude-work", "Claude Code", new DesignMeterViewModel(), false));

            Assert.Equal("claude-max", current.TextOf("Connection"));
            Assert.Contains("current", current.Find<Border>("Lane").Classes);
            Assert.DoesNotContain("current", other.Find<Border>("Lane").Classes);
            Assert.IsType<DesignMeterViewModel>(current.Find<ContentControl>("Meter").Content);
        }, TestContext.Current.CancellationToken);
}
