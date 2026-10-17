using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class DocsToggleViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheGlyphShowsSelectedWhileThePaneIsOpenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDocsToggleViewModel());

            Assert.Contains("selected", view.Find<Button>("Docs").Classes);
        }, TestContext.Current.CancellationToken);
}
