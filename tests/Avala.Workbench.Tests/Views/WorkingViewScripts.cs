using Avala.Components.UI.Elapsed;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Views;

public sealed class WorkingViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheRowAnimatesItsMarkAndCountsTheTurnsTimeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorkingViewModel { Since = DateTimeOffset.UnixEpoch, Verb = "Writing sistema-solar.html" });
            ElapsedText.SetClock(view.Window, new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(23)));
            view.Settle();

            Assert.Equal(("Writing sistema-solar.html", "23s"), (view.TextOf("Verb"), view.TextOf("Elapsed")));
            Assert.Equal((true, false, true), (view.Shows("Mark"), view.Shows("Still"), view.Shows("Shimmer")));
        }, Cancellation);

    [Fact]
    public Task WaitingForYouTheRowHoldsStillWithAnAttentionDotAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorkingViewModel { Verb = "Waiting for you", IsWaiting = true });

            Assert.Equal((false, true, false), (view.Shows("Mark"), view.Shows("Still"), view.Shows("Shimmer")));
        }, Cancellation);

    [Fact]
    public Task NothingShowsOnceTheAgentStopsWorkingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorkingViewModel { IsShown = false });

            Assert.False(view.Shows("Verb"));
        }, Cancellation);
}
