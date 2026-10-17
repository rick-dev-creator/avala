using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class JobHistoryViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheHistoryListsItsStepsAndSummarizesTheChosenVersionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobHistoryViewModel(), Avalonia.Styling.ThemeVariant.Dark, 400, 700);

            Assert.Equal(["Plan v1 · proposed", "Plan v2 · deviation"], view.All<TextBlock>().Where(text => text.Name == "StepTitle").Select(text => text.Text));
            Assert.Equal(("Plan v2 · deviation", "v2-deviation.md · plans/v2.md"), (view.TextOf("Heading"), view.TextOf("Records")));
            Assert.True(view.Find<Button>("OpenWriter").IsVisible);
            Assert.Equal(["+ Idempotency store", "~ Payments"], view.All<TextBlock>().Where(text => text.Classes.Contains("mono") && text.Name is null).Select(text => text.Text));
        }, TestContext.Current.CancellationToken);
}
