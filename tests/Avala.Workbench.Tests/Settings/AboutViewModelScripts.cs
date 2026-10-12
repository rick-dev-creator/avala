using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Machine;
using Avala.Workbench.Onboarding;
using Avala.Workbench.Settings;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Tests.Updates;
using Avala.Workbench.Updates;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Settings;

public sealed class AboutViewModelScripts
{
    private static readonly AvalaPaths Paths = new(Path.Combine("data", "Avala"));

    private static readonly AvalaBuild Build = new("0.9.0-beta.1", "5c371e6a1b2d3e4f5a6b");

    [Fact]
    public void ItShowsTheVersionTheShortCommitAndTheLogFolder() =>
        ViewModelScript.Given(new AboutViewModel(Update(Build), new DesignTelemetryViewModel(), new SettingsFiles(new FakeOpener(), Paths), new AboutLinks(new FakeLinks(), new WeakReferenceMessenger())))
            .Then(about => Assert.Equal(("0.9.0-beta.1", "5c371e6a1b2d", Path.Combine("data", "Avala", "logs"), string.Empty), (about.Version, about.Commit, about.LogFolder.Value, about.Note.Show(hidden: false))));

    [Fact]
    public void ABuildWithoutACommitSaysItIsUnknown() =>
        ViewModelScript.Given(new AboutViewModel(Update(new AvalaBuild("1.0.0", Option<string>.None)), new DesignTelemetryViewModel(), new SettingsFiles(new FakeOpener(), Paths), new AboutLinks(new FakeLinks(), new WeakReferenceMessenger())))
            .Then(about => Assert.Equal("unknown", about.Commit));

    [Fact]
    public async Task OpenOpensTheLogFolderThroughTheFileOpener()
    {
        var files = new FakeOpener();
        var about = new AboutViewModel(Update(Build), new DesignTelemetryViewModel(), new SettingsFiles(files, Paths), new AboutLinks(new FakeLinks(), new WeakReferenceMessenger()));

        await about.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal([Paths.Logs], files.Opened);
        Assert.True(about.Note.IsEmpty);
    }

    [Theory]
    [InlineData(nameof(FileOpenError.Unavailable), "Nothing on this computer opens folders from Avala. The log folder is ")]
    [InlineData(nameof(FileOpenError.Refused), "The system refused to open the log folder. It is ")]
    [InlineData(nameof(FileOpenError.Uncreatable), "The log folder could not be created at ")]
    public async Task AFolderThePlatformCannotOpenSaysWhyAndWhereItIs(string error, string said)
    {
        var about = new AboutViewModel(Update(Build), new DesignTelemetryViewModel(), new SettingsFiles(new FakeOpener { Refusal = Enum.Parse<FileOpenError>(error) }, Paths), new AboutLinks(new FakeLinks(), new WeakReferenceMessenger()));

        await about.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal($"{said}{Paths.Logs}.", about.Note.Show(hidden: false));
    }

    [Fact]
    public async Task RepositoryAndLicenseOpenTheirLinksAndAnUnopenedLinkGivesItsAddress()
    {
        var links = new FakeLinks();
        var about = new AboutViewModel(Update(Build), new DesignTelemetryViewModel(), new SettingsFiles(new FakeOpener(), Paths), new AboutLinks(links, new WeakReferenceMessenger()));
        var refused = new AboutViewModel(Update(Build), new DesignTelemetryViewModel(), new SettingsFiles(new FakeOpener(), Paths), new AboutLinks(new FakeLinks { Refusal = FileOpenError.Unavailable }, new WeakReferenceMessenger()));

        await about.OpenRepositoryCommand.ExecuteAsync(null);
        await about.OpenLicenseCommand.ExecuteAsync(null);
        await refused.OpenLicenseCommand.ExecuteAsync(null);

        Assert.Equal([AvalaBuild.Repository, AvalaBuild.License], links.Opened);
        Assert.Equal($"The browser could not be opened. The address is {AvalaBuild.License}.", refused.Note.Show(hidden: false));
    }

    [Fact]
    public void ShowWelcomeTourAsksForTheTourFromItsFirstStep()
    {
        var messenger = new WeakReferenceMessenger();
        var asked = new List<TourRequested>();
        messenger.Register<List<TourRequested>, TourRequested>(asked, (requests, request) => requests.Add(request));
        var about = new AboutViewModel(Update(Build), new DesignTelemetryViewModel(), new SettingsFiles(new FakeOpener(), Paths), new AboutLinks(new FakeLinks(), messenger));

        about.ShowTourCommand.Execute(null);

        Assert.Equal([new TourRequested(0)], asked);
    }

    private static UpdateViewModel Update(AvalaBuild build) => new(build, new FakeUpdates(), new FakeLinks());
}
