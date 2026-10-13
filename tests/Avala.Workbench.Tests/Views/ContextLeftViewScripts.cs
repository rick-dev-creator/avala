using Avala.Testing.UI;
using Avala.Workbench.Usage;

namespace Avala.Workbench.Tests.Views;

public sealed class ContextLeftViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheGaugeShowsTheShareOfTheContextInUseAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignContextLeftViewModel());

            Assert.Equal(("30% context left", true, false), (view.TextOf("Share"), view.Shows("Gauge"), view.HasClass("Ring", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ANearlyFullContextTurnsTheGaugeAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Gauge(isShown: true, isNear: true));

            Assert.Equal((true, true), (view.HasClass("Ring", "attention"), view.HasClass("Share", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAReportTheGaugeIsHiddenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Gauge(isShown: false, isNear: false));

            Assert.False(view.Shows("Gauge"));
        }, TestContext.Current.CancellationToken);

    private sealed class Gauge(bool isShown, bool isNear) : IContextLeftViewModel
    {
        public bool IsShown { get; } = isShown;

        public string Share => "7% context left";

        public double Used => 0.93;

        public bool IsNear { get; } = isNear;

        public bool WasCompacted => false;

        public string Tokens => "185,000 of 200,000 tokens in use";

        public string Model => string.Empty;

        public string Compaction => string.Empty;
    }
}
