using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avala.Workbench.Tests.Updates;
using Avala.Workbench.Updates;

namespace Avala.Workbench.Tests.Views;

public sealed class AboutViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionShowsTheVersionTheCommitAndTheLogFolderAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAboutViewModel());

            Assert.Equal(("0.9.0-beta.1", "5c371e6a1b2d", "/home/dana/.local/share/Avala/logs"), (view.TextOf("Version"), view.TextOf("Commit"), view.TextOf("LogFolder")));
            Assert.Superset(new HashSet<string>(["About", "Diagnostics", "Repository", "License (MIT)", "Open", "Updates", "Version 0.9.0 is available.", "Download", "Check now"]), view.VisibleTexts.ToHashSet());
            Assert.False(view.Shows("Note"));
        }, Cancellation);

    [Fact]
    public Task ClickingOpenAndTheLinksReachesTheOpenersAndARefusalIsShownAsync() =>
        ui.RunAsync(() =>
        {
            var files = new FakeOpener { Refusal = FileOpenError.Unavailable };
            var links = new FakeLinks();
            var paths = new AvalaPaths(Path.Combine("data", "Avala"));
            var view = Screen.Show(new AboutViewModel(new UpdateViewModel(new AvalaBuild("1.0.0", "abc"), new FakeUpdates(), links), paths, files, links));

            view.Click("OpenRepository");
            view.Click("OpenLicense");
            view.Click("OpenLogFolder");
            view.Settle();

            Assert.Equal([AvalaBuild.Repository, AvalaBuild.License], links.Opened);
            Assert.Equal([paths.Logs], files.Opened);
            Assert.True(view.Shows("Note"));
            Assert.StartsWith("Nothing on this computer opens folders", view.TextOf("NoteText"), StringComparison.Ordinal);
        }, Cancellation);
}
