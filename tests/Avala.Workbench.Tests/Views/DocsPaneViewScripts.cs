using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class DocsPaneViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePaneShowsTheJobsFeatureWithItsCompactPlayerItsChangesAndTheRepositorysOtherDocsAsync() =>
        ui.RunAsync(() =>
        {
            using var pane = new DesignDocsPaneViewModel();
            var view = Screen.Show(pane, ThemeVariant.Dark, 420, 900);

            Assert.Equal(("Payments on stripe-go v79", "writing"), (view.TextOf("Feature"), view.TextOf("State")));
            Assert.True(view.Shows("Hud"));
            Assert.False(view.Shows("Document"));
            Assert.Equal(["Change request #1"], view.All<TextBlock>().Where(text => text.Name == "RequestTitle" && text.IsEffectivelyVisible).Select(text => text.Text));
            Assert.Equal(["Payout scheduler"], view.All<TextBlock>().Where(text => text.Name == "FeatureTitle").Select(text => text.Text));
        }, TestContext.Current.CancellationToken);
}
