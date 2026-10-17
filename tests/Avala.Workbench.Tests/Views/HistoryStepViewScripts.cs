using Avala.Testing.UI;
using Avala.Workbench.Docs;

namespace Avala.Workbench.Tests.Views;

public sealed class HistoryStepViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AVersionShowsAsARowAndARequestAsAQuoteAsync() =>
        ui.RunAsync(() =>
        {
            var version = Screen.Show(new DesignHistoryStepViewModel());
            var request = Screen.Show(new DesignHistoryStepViewModel("Change request #1", "“Once only”", false, false));

            Assert.Equal(("Plan v2 · deviation", false), (version.TextOf("StepTitle"), version.Shows("RequestText")));
            Assert.Equal(("“Once only”", false), (request.TextOf("RequestText"), request.Shows("Step")));
        }, TestContext.Current.CancellationToken);
}
