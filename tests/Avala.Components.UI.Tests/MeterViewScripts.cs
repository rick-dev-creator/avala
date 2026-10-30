using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Tests;

public sealed class MeterViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AMeterNearItsThresholdReadsInAmberWithItsTickAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new DesignMeterViewModel());

            Assert.Equal("claude-work · 5h", view.TextOf("Label"));
            Assert.Equal("88% · resets 16:20", view.TextOf("Reading"));
            Assert.True(view.Shows("Tick"));
            Assert.Equal(Color.Parse("#FFB547"), Brush(view.Find<TextBlock>("Reading").Foreground));
            Assert.Equal(Color.Parse("#FFB547"), Brush(view.Find<ProgressBar>("Fill").Foreground));
        }, Cancellation);

    [Fact]
    public Task AMeterFarFromItsThresholdStaysNeutralAsync() =>
        ui.RunAsync(() =>
        {
            var meter = new MeterViewModel("claude-personal · 5h", 0.9);
            meter.Show(0.31);
            var view = ViewScript.Show(meter);

            Assert.Equal("31%", view.TextOf("Reading"));
            Assert.Equal(Color.Parse("#9BA3AA"), Brush(view.Find<ProgressBar>("Fill").Foreground));
            Assert.Equal(0.31, view.Find<ProgressBar>("Fill").Value);
        }, Cancellation);

    [Fact]
    public Task AMeterWithoutAThresholdShowsNoTickAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new MeterViewModel("Memory", Option<double>.None));

            Assert.False(view.Shows("Tick"));
        }, Cancellation);

    [Fact]
    public Task ANewReadingShowsAsItArrivesAsync() =>
        ui.RunAsync(() =>
        {
            var meter = new MeterViewModel("claude-work · 5h", 0.9);
            var view = ViewScript.Show(meter);

            meter.Show(0.92, "resets 16:20");
            view.Settle();

            Assert.Equal("92% · resets 16:20", view.TextOf("Reading"));
            Assert.Equal(Color.Parse("#FFB547"), Brush(view.Find<TextBlock>("Reading").Foreground));
        }, Cancellation);

    [Fact]
    public Task AMeterFillsInSegmentsThatLightUpToItsNewReadingAsync() =>
        ui.RunAsync(() =>
        {
            var meter = new MeterViewModel("claude-work · 5h", 0.9);
            meter.Show(0.2);
            var view = ViewScript.Show(meter);
            var fill = view.Find<ProgressBar>("Fill");
            view.Elapse(TimeSpan.FromMilliseconds(700));
            var shown = fill.Value;

            meter.Show(0.8, "resets 16:20");
            view.Settle();
            var rising = fill.Value;
            view.Elapse(TimeSpan.FromMilliseconds(700));

            Assert.IsType<DrawingBrush>(fill.OpacityMask);
            Assert.Equal(0.2, shown, 3);
            Assert.InRange(rising, 0.2, 0.79);
            Assert.Equal(0.8, fill.Value, 3);
        }, Cancellation);

    private static Color Brush(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
