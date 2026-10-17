using Avala.Testing.UI;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Views;

public sealed class DocsLineViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AVersionLineShowsItsFileAndWhatItIsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDocsLineViewModel());

            Assert.Equal(("v2-deviation.md", "Refund webhooks · deviation"), (view.TextOf("DocsFile"), view.TextOf("DocsFact")));
        }, TestContext.Current.CancellationToken);
}
