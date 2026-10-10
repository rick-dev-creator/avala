using Avala.Testing.UI;
using Avala.Workbench.Triggers;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Avala.Workbench.Tests.Views;

public sealed class TriggerItemViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsTriggerItsNextRunItsTargetAndItsRunsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTriggerItemViewModel()).Settle();

            Assert.Equal(
                ("nightly-deps", "Weekdays at 02:30", "~/code/ledger-api", "Next · Mon 12 Oct 02:30", "· 0 of 1 running", "· Starts a job · supervised at most"),
                (view.TextOf("TriggerName"), view.TextOf("Fires"), view.TextOf("Repository"), view.TextOf("Next"), view.TextOf("Running"), view.TextOf("Target")));
            Assert.Equal(2, view.Find<ItemsControl>("Runs").ItemCount);
            Assert.Equal("Disable", view.Find<Button>("Toggle").Content);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADisabledRowIsMarkedOffAndOffersToEnableItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTriggerItemViewModel() with { IsEnabled = false, Next = "Disabled" }).Settle();

            Assert.Equal(("Enable", "Disabled"), (view.Find<Button>("Toggle").Content, view.TextOf("Next")));
            Assert.Contains("off", view.Find("Toggle").GetLogicalAncestors().OfType<UserControl>().First().Classes);
        }, TestContext.Current.CancellationToken);
}
