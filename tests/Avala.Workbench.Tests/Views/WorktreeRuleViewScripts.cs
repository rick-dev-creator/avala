using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class WorktreeRuleViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsMomentAndTheChoiceMadeForItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeRuleViewModel());

            Assert.Equal(("Archived", "You archived its conversation."), (view.TextOf("Title"), view.TextOf("Explanation")));
            Assert.Equal("Free it at once", view.Find<ComboBox>("Choices").SelectedItem);
        }, TestContext.Current.CancellationToken);
}
