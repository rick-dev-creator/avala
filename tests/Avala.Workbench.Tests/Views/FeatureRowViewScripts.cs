using Avala.Testing.UI;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Tests.Views;

public sealed class FeatureRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsTheFeatureAndItsVersionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignFeatureRowViewModel("Refund webhooks", "2 versions"));

            Assert.Equal(("Refund webhooks", "2 versions"), (view.TextOf("FeatureTitle"), view.TextOf("FeatureFact")));
        }, TestContext.Current.CancellationToken);
}
