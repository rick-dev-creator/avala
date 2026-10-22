using Avala.Testing.UI;
using Avala.Workbench.Triggers;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class TriggerItemViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsTriggerItsNextRunItsTargetAndItsRunsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTriggerItemViewModel()).Settle();

            Assert.Equal(
                ("nightly-deps", "Weekdays at 02:30", "~/code/ledger-api", "Next · Mon 12 Oct 02:30", " · 0 of 1 running", " · Starts a job · supervised at most"),
                (view.TextOf("TriggerName"), view.TextOf("Fires"), view.TextOf("Repository"), view.TextOf("Next"), view.TextOf("Running"), view.TextOf("Target")));
            Assert.Equal(2, view.Find<ItemsControl>("Runs").ItemCount);
            Assert.Equal("Disable", view.Find<Button>("Toggle").Content);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARunLineShownWholeOffersNoTooltipAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTriggerItemViewModel(), Avalonia.Styling.ThemeVariant.Dark, 1000, 400).Settle();

            Assert.All(RunLines(view), line => Assert.Null(ToolTip.GetTip(line)));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARunLineCutByTheRowOffersItselfWholeAsync() =>
        ui.RunAsync(() =>
        {
            var run = "Fri 9 Oct 02:30 · on schedule · job submitted for the nightly dependency refresh of every service in the ledger";
            var view = Screen.Show(new DesignTriggerItemViewModel() with { Runs = [run] }, Avalonia.Styling.ThemeVariant.Dark, 320, 400).Settle();

            Assert.Equal(run, ToolTip.GetTip(Assert.Single(RunLines(view))));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheNextRunTheRunningCountAndTheTargetReadAsOneLineWithoutExtraGapsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTriggerItemViewModel()).Settle();
            var (next, running, target) = (view.Find("Next"), view.Find("Running"), view.Find("Target"));

            Assert.Equal((next.Bounds.Right, running.Bounds.Right), (running.Bounds.X, target.Bounds.X));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADisabledRowIsMarkedOffAndOffersToEnableItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignTriggerItemViewModel() with { IsEnabled = false, Next = "Disabled" }).Settle();

            Assert.Equal(("Enable", "Disabled"), (view.Find<Button>("Toggle").Content, view.TextOf("Next")));
            Assert.Contains("off", view.Find("Toggle").GetLogicalAncestors().OfType<UserControl>().First().Classes);
        }, TestContext.Current.CancellationToken);

    private static IReadOnlyList<TextBlock> RunLines(ViewScript view) => [.. view.Find<ItemsControl>("Runs").GetVisualDescendants().OfType<TextBlock>()];
}
