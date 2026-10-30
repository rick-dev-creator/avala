using Avala.Testing.UI;
using Avala.Workbench.Resources;

namespace Avala.Workbench.Tests.Views;

public sealed class WorktreeRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsTitleItsDetailAndItsAmountAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeRowViewModel());

            Assert.Equal(("Add GitHub login", "82% build output", "1.9 GB"), (view.TextOf("Title"), view.TextOf("Detail"), view.TextOf("Amount")));
        }, TestContext.Current.CancellationToken);
}
