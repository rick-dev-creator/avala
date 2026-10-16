using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class CiViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AFailedCheckShowsItsCardAndAsksTheAgentToFixItAsync() =>
        ui.RunAsync(() =>
        {
            var asked = new List<string?>();
            var ci = new DesignCiViewModel
            {
                AskCommand = new AsyncRelayCommand<string?>(text =>
                {
                    asked.Add(text);

                    return Task.CompletedTask;
                }),
            };
            var view = Screen.Show(ci);

            view.Click("Ask");

            Assert.Equal(("CI failed: build (windows-latest)", false, true), (view.TextOf("Headline"), view.Shows("Passed"), view.Shows("Card")));
            Assert.Equal([ci.Request], asked);
        }, Cancellation);

    [Fact]
    public Task ChecksThatPassedShowOnlyAQuietLineAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCiViewModel { IsFailed = false, CanAsk = false, Failing = [], Headline = "All 3 checks of pull request #12 passed" });

            Assert.Equal(("All 3 checks of pull request #12 passed", false), (view.TextOf("Passed"), view.Shows("Card")));
        }, Cancellation);
}
