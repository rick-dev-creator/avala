using Avala.Testing.UI;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Views;

public sealed class ChangeLineViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChangeLineSaysItWasRecordedAndWhatItProducedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignChangeLineViewModel());

            Assert.Equal(("Change request #1 recorded", "steer → v2"), (view.TextOf("ChangeText"), view.TextOf("ChangeFact")));
        }, TestContext.Current.CancellationToken);
}
