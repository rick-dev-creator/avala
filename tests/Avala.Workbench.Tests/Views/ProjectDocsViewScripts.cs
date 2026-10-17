using Avala.Testing.UI;
using Avala.Workbench.Sidebar;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class ProjectDocsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AProjectsDocsShowTheirCountAndListTheFeaturesWhenOpenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectDocsViewModel());
            var closed = Screen.Show(new DesignProjectDocsViewModel { IsOpen = false });

            Assert.Equal("2", view.TextOf("DocsCount"));
            Assert.Equal(["Payments on stripe-go v79", "Refund webhooks"], view.All<TextBlock>().Where(text => text.Name == "FeatureTitle").Select(text => text.Text));
            Assert.False(closed.Shows("Features"));
        }, Cancellation);

    [Fact]
    public Task ARepositoryWithoutDocsShowsNoDocsRowAsync() =>
        ui.RunAsync(() => Assert.False(Screen.Show(new DesignProjectDocsViewModel { IsShown = false }).Shows("DocsToggle")), Cancellation);

    [Fact]
    public Task TheProjectGroupShowsItsDocsUnderItsJobsAsync() =>
        ui.RunAsync(() => Assert.True(Screen.Show(new DesignProjectGroupViewModel()).Shows("DocsToggle")), Cancellation);
}
