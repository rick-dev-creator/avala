using Avala.Testing.UI;
using Avala.Workbench.Triggers;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class LoopItemViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsStatusRepositoryDigestAndIterationsAndOffersPauseAndStopWhileItRunsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLoopItemViewModel()).Settle();

            Assert.Equal(
                ("Running · 3 of 20 iterations done", "~/code/ledger-api", "2 approved on their own · 1 waiting for you · spent 0.42 USD"),
                (view.TextOf("LoopStatus"), view.TextOf("LoopRepository"), view.TextOf("LoopSummary")));
            Assert.Equal(3, view.Find<ItemsControl>("LoopIterations").ItemCount);
            Assert.Equal((true, false, true, false), (view.Shows("Pause"), view.Shows("Resume"), view.Shows("Stop"), view.HasClass("LoopStatus", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnEndedLoopThatNeedsAttentionShowsItsStatusInTheAttentionStyleWithoutCommandsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLoopItemViewModel() with { Status = "Ended · breaker tripped: the same failure, 2 of 2", NeedsAttention = true }).Settle();

            Assert.Equal((true, false, false, false), (view.HasClass("LoopStatus", "attention"), view.Shows("Pause"), view.Shows("Resume"), view.Shows("Stop")));
        }, TestContext.Current.CancellationToken);
}
