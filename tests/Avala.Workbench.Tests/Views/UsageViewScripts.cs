using Avala.Components.UI.Graphs;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Usage;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Workbench.Tests.Views;

public sealed class UsageViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageShowsConnectionsJobsTokenWindowsAndInterventionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel());

            Assert.Equal((2, 2, 6, 2), (view.Find<ItemsControl>("Connections").ItemCount, view.Find<ItemsControl>("Windows").ItemCount, view.Find<ItemsControl>("Jobs").ItemCount, view.Find<ItemsControl>("InterventionList").ItemCount));
            Assert.Equal((false, false, true, "All recorded usage, kept across restarts"), (view.Shows("NoConnections"), view.Shows("NoJobs"), view.Shows("InterventionsGroup"), view.TextOf("Scope")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BeforeAnyUsageThePageSaysSoAndHidesInterventionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new EmptyUsage());

            Assert.Equal((true, true, false), (view.Shows("NoConnections"), view.Shows("NoJobs"), view.Shows("InterventionsGroup")));
        }, TestContext.Current.CancellationToken);

    private sealed class EmptyUsage : IUsageViewModel
    {
        public string Title => "Usage";

        public string Scope => "All recorded usage, kept across restarts";

        public IReadOnlyList<IConnectionMeterViewModel> Connections => [];

        public IReadOnlyList<IUsageWindowViewModel> Windows => [];

        public IReadOnlyList<IJobMeterViewModel> Jobs => [];

        public IReadOnlyList<IInterventionViewModel> Interventions => [];
    }
}

public sealed class ConnectionMeterViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionShowsItsTotalCostItsTokensCapsAndLimitWindowsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionMeterViewModel());

            Assert.Equal(("claude-work", "9.86 USD in total", 2), (view.TextOf("ConnectionName"), view.TextOf("Cost"), view.Find<ItemsControl>("Limits").ItemCount));
            Assert.Equal((false, false), (view.Shows("Unpriced"), view.Shows("NoLimits")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AConnectionWithoutLimitsSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionMeterViewModel("pi-local", "no cost", "0 tokens", []));

            Assert.True(view.Shows("NoLimits"));
        }, TestContext.Current.CancellationToken);
}

public sealed class LimitViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ALimitNearItsHoldTurnsAmberMarksTheThresholdAndExplainsItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel());

            Assert.Equal(("5-hour window", "88%", " · resets 2026-10-09 16:20"), (view.TextOf("Window"), view.TextOf("UsedText"), view.TextOf("Resets")));
            Assert.Equal((0.88, 0.9), (view.Find<ProgressBar>("Used").Value, view.Find<ProgressBar>("Hold").Value));
            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<ProgressBar>("Used").Foreground).Color);
            Assert.Equal((true, true, "Jobs on this connection hold at the 90% threshold."), (view.HasClass("UsedText", "attention"), view.Shows("HoldAt"), view.TextOf("HoldAt")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitFarFromItsHoldStaysNeutralAndQuietAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("7d", "7-day window", 0.43, "43%", "resets 2026-10-13 09:00", 0.9, false));

            Assert.Equal((false, false), (view.HasClass("UsedText", "attention"), view.Shows("HoldAt")));
            Assert.Equal(Color.Parse("#A3A3AD"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<ProgressBar>("Used").Foreground).Color);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitPastItsHoldFillsTheBarWithoutOverflowingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("5h", "5-hour window", 1, "104%", "resets 16:20", 0.9, true));

            Assert.Equal((1d, "104%", true), (view.Find<ProgressBar>("Used").Value, view.TextOf("UsedText"), view.HasClass("UsedText", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitWithoutAHoldShowsNoMarkerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("5h", "5-hour window", 0.95, "95%", "resets 16:20", 0, false));

            Assert.Equal((false, false), (view.Shows("Hold"), view.HasClass("UsedText", "attention")));
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageWindowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TokensByTypeStayFoldedUntilOpenedThenShowTheirSharesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageWindowViewModel());
            var folded = view.Shows("Turns");

            view.Find<Expander>("Disclosure").IsExpanded = true;
            view.Settle();
            var shares = view.Find<ShareBar>("Shares").Children.Select(child => child.Bounds.Width).ToList();

            Assert.False(folded);
            Assert.Equal(("Tokens by type · Today", "1,686,520 tokens", true), (view.TextOf("Label"), view.TextOf("Tokens"), view.Shows("Turns")));
            Assert.Contains("cache read 1,204,300", view.VisibleTexts);
            Assert.True(shares[2] > shares[0] && shares[0] > shares[1]);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task UnpricedReportsAreNotedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageWindowViewModel("Last 7 days", "14 USD", 1, 1, 1, 1, 1, "2 usage reports had no cost"));
            view.Find<Expander>("Disclosure").IsExpanded = true;
            view.Settle();

            Assert.Equal("2 usage reports had no cost", view.TextOf("Unpriced"));
        }, TestContext.Current.CancellationToken);
}

public sealed class JobMeterViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AJobShowsItsConnectionAndCostAgainstItsCapAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel());

            Assert.Equal(("Rate-limit POST /login", "claude-work", "1.12 USD", " / 3 USD", 0.37), (view.TextOf("Title"), view.TextOf("Connection"), view.TextOf("Cost"), view.TextOf("Cap"), view.Find<ProgressBar>("Used").Value));
            Assert.Equal((false, false), (view.Shows("Interventions"), view.HasClass("Used", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobAtItsCapTurnsAmberAndOneHeldSaysHowOftenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "claude-personal", "3.04 USD", 1, "3 USD", 2));

            Assert.Equal((true, "held 2 times"), (view.HasClass("Used", "attention"), view.TextOf("Interventions")));
            Assert.True(view.HasClass("Interventions", "attention"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobWithoutACapHasNoBarAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.JpyRounding, "Fix JPY rounding", JobStatus.Running, "claude-work", "0.41 USD", 0, string.Empty, 0));

            Assert.Equal((false, false), (view.Shows("Used"), view.Shows("Cap")));
        }, TestContext.Current.CancellationToken);
}

public sealed class InterventionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnInterventionShowsWhenWhatAndWhyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignInterventionViewModel());

            Assert.Equal(("2026-10-09 14:52", "held: Stalled", "— silent for 600s, window 600s"), (view.TextOf("At"), view.TextOf("Reason"), view.TextOf("Detail")));
        }, TestContext.Current.CancellationToken);
}
