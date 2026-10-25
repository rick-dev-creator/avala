using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class WorktreeLimitViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ALimitShowsWhatItBoundsAndTheChoiceMadeForItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeLimitViewModel());

            Assert.Equal((WorktreePhrases.BudgetTitle, WorktreePhrases.BudgetNote), (view.TextOf("Title"), view.TextOf("Explanation")));
            Assert.Equal("20 GB", view.Find<ComboBox>("Choices").SelectedItem);
        }, TestContext.Current.CancellationToken);
}
