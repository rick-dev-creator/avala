using Avala.Sdk;
using Avala.Sdk.Updates;
using Avala.Testing.UI;
using Avala.Workbench.Tests.Updates;
using Avala.Workbench.Updates;

namespace Avala.Workbench.Tests.Views;

public sealed class UpdateViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task CheckingNowShowsTheNewerVersionAndDownloadOpensItsPageAsync() =>
        ui.RunAsync(async () =>
        {
            var links = new FakeLinks();
            var update = new UpdateViewModel(new AvalaBuild("0.1.0", "abc"), new FakeUpdates { Latest = FakeUpdates.Of(UpdateStatus.UpToDate) }, links);
            var view = Screen.Show(update);
            var before = (view.TextOf("Status"), view.Shows("OpenRelease"));

            view.Click("Check");
            await update.CheckCommand.ExecutionTask!;
            view.Settle();
            var after = (view.TextOf("Status"), view.Shows("OpenRelease"));
            view.Click("OpenRelease");
            await update.OpenReleaseCommand.ExecutionTask!;

            Assert.Equal(("Avala is up to date.", false), before);
            Assert.Equal(("Version 0.2.0 is available.", true), after);
            Assert.Equal([FakeUpdates.Newer.Release], links.Opened);
        }, Cancellation);
}
