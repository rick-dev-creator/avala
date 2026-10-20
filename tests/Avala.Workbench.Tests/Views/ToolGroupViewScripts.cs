using Avala.Testing.UI;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Views;

public sealed class ToolGroupViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARunningRunSpinsAndShowsWhatItIsRunningBesideWhatItDidAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignToolGroupViewModel());

            Assert.Equal(("4 commands, 2 edits", "go test ./internal/money/...", true, false), (view.TextOf("Title"), view.TextOf("Current"), view.Shows("Spinner"), view.Shows("Icon")));
        }, Cancellation);

    [Fact]
    public Task AFinishedRunShowsItsKindIconAndNothingRunningAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignToolGroupViewModel { IsRunning = false });

            Assert.Equal((false, false, true), (view.Shows("Current"), view.Shows("Spinner"), view.Shows("Icon")));
        }, Cancellation);
}
