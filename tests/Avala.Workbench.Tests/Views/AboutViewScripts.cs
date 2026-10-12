using Avala.Sdk;
using Avala.Sdk.Telemetry;
using Avala.Testing.UI;
using Avala.Workbench.Machine;
using Avala.Workbench.Onboarding;
using Avala.Workbench.Settings;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Tests.Telemetry;
using Avala.Workbench.Tests.Updates;
using Avala.Workbench.Updates;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Views;

public sealed class AboutViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionShowsTheVersionTheCommitAndTheLogFolderUnderTheMaskedHomeAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAboutViewModel());

            Assert.Equal(("0.9.0-beta.1", "5c371e6a1b2d", "~/.local/share/Avala/logs"), (view.TextOf("Version"), view.TextOf("Commit"), view.TextOf("LogFolder")));
            Assert.Superset(new HashSet<string>(["About", "Diagnostics", "Repository", "License (MIT)", "Open", "Updates", "Version 0.9.0 is available.", "Download", "Check now"]), view.VisibleTexts.ToHashSet());
            Assert.False(view.Shows("Note"));
        }, Cancellation);

    [Fact]
    public Task ClickingOpenTheLinksAndTheTourReachesTheOpenersAndARefusalIsShownAsync() =>
        ui.RunAsync(() =>
        {
            var files = new FakeOpener { Refusal = FileOpenError.Unavailable };
            var links = new FakeLinks();
            var paths = new AvalaPaths(Path.Combine("data", "Avala"));
            var messenger = new WeakReferenceMessenger();
            var tours = new List<TourRequested>();
            messenger.Register<List<TourRequested>, TourRequested>(tours, (asked, request) => asked.Add(request));
            var view = Screen.Show(new AboutViewModel(new UpdateViewModel(new AvalaBuild("1.0.0", "abc"), new FakeUpdates(), links), new TelemetryViewModel(new FakeTelemetry { State = TelemetryState.NotConfigured }), new SettingsFiles(files, paths), new AboutLinks(links, messenger)));

            view.Click("OpenRepository");
            view.Click("OpenLicense");
            view.Click("OpenLogFolder");
            view.Click("ShowTour");
            view.Settle();

            Assert.Single(tours);

            Assert.Equal([AvalaBuild.Repository, AvalaBuild.License], links.Opened);
            Assert.Equal([paths.Logs], files.Opened);
            Assert.True(view.Shows("Note"));
            Assert.StartsWith("Nothing on this computer opens folders", view.TextOf("NoteText"), StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task ALogFolderThatCannotBeOpenedIsNamedUnderTheMaskedHomeAsync() =>
        ui.RunAsync(() =>
        {
            var links = new FakeLinks();
            var paths = new AvalaPaths("/home/ada/.local/share/Avala");
            var view = Screen.Show(new AboutViewModel(new UpdateViewModel(new AvalaBuild("1.0.0", "abc"), new FakeUpdates(), links), new TelemetryViewModel(new FakeTelemetry { State = TelemetryState.NotConfigured }), new SettingsFiles(new FakeOpener { Refusal = FileOpenError.Refused }, paths), new AboutLinks(links, new WeakReferenceMessenger())));

            view.Click("OpenLogFolder");
            view.Settle();

            Assert.Equal($"The system refused to open the log folder. It is ~{paths.Logs["/home/ada".Length..]}.", view.TextOf("NoteText"));
        }, Cancellation);
}
