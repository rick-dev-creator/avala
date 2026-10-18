using Avala.Components.Zooming;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class ZoomViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheButtonsZoomOutShowTheActualSizeZoomInAndFitAgainAsync() =>
        ui.RunAsync(() =>
        {
            var zoom = new ZoomViewModel { Scale = 0.64 };
            var view = ViewScript.Show(zoom);
            var fitted = (view.TextOf("Percent"), view.Find<Button>("Fit").IsEffectivelyEnabled);

            view.Click("ZoomIn");
            var zoomedIn = view.TextOf("Percent");
            view.Click("ActualSize");
            var actual = (view.TextOf("Percent"), view.Find<Button>("ActualSize").IsEffectivelyEnabled);
            view.Click("ZoomOut");
            var zoomedOut = view.TextOf("Percent");
            view.Click("Fit");

            Assert.Equal(("64%", false), fitted);
            Assert.Equal("80%", zoomedIn);
            Assert.Equal(("100%", false), actual);
            Assert.Equal("80%", zoomedOut);
            Assert.Equal((true, false), (zoom.IsFitted, view.Find<Button>("Fit").IsEffectivelyEnabled));
        }, Cancellation);
}
