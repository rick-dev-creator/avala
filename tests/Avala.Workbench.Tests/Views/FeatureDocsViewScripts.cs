using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class FeatureDocsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageNamesTheFeatureAndItsRepositoryAboveThePlayerAsync() =>
        ui.RunAsync(() =>
        {
            using var page = new DesignFeatureDocsViewModel();
            var view = Screen.Show(page, ThemeVariant.Dark, 1240, 780);

            Assert.Equal(("Payments on stripe-go v79", "ledger-api · payments-on-stripe-go-v79"), (view.TextOf("FeatureTitle"), view.TextOf("FeatureWhere")));
            Assert.True(view.Shows("Hud"));
        }, TestContext.Current.CancellationToken);
}
