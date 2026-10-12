using Avala.Shell;
using Avala.Testing.UI;

namespace Avala.Host.Tests;

public sealed class DataFolderInUseViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ItSaysAvalaIsAlreadyRunningOnTheFolderUnderTheMaskedHomeAndQuitsOnRequestAsync() =>
        ui.RunAsync(() =>
        {
            var quits = 0;
            var refusal = new DataFolderInUseViewModel("/home/dana/.local/share/Avala");
            refusal.QuitRequested += (_, _) => quits++;
            var view = ViewScript.Present(new DataFolderInUseView(), refusal);

            Assert.Equal(("Avala is already running", "~/.local/share/Avala"), (view.TextOf("Heading"), view.TextOf("Folder")));
            Assert.StartsWith("Another Avala already runs on this data folder", view.TextOf("Reason"), StringComparison.Ordinal);
            Assert.NotNull(view.Window.Icon);

            view.Click("Quit");

            Assert.Equal(1, quits);
        }, Cancellation);
}
