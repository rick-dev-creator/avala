using Avala.Runtime.Updates;
using Avala.Sdk;
using Avala.Sdk.Updates;
using Avala.Testing;

namespace Avala.Runtime.Tests.Updates;

public sealed class InstalledUpdateTests
{
    private static readonly AvailableUpdate Newer = new("0.2.0", new Uri("https://github.com/rick-dev-creator/avala/releases/tag/v0.2.0"), InstallsInPlace: true);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInstalledAvalaIsOfferedTheNewerVersionToInstallInPlaceWithoutListingTheReleasesAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        using var releases = FakeReleases.Listing(FakeReleases.Release("v0.3.0"));
        var check = Check(data, FakeInstaller.Offering("0.2.0"), bus, releases);

        var state = await check.CheckAsync(Cancellation);

        Assert.Equal(new UpdateState(UpdateStatus.Available, Newer), state);
        Assert.Equal([new UpdateFound(Newer)], bus.Published);
        Assert.Empty(releases.Requests);
    }

    [Theory]
    [InlineData(true, nameof(UpdateStatus.UpToDate))]
    [InlineData(false, nameof(UpdateStatus.Unreachable))]
    public async Task AnInstalledAvalaWithNothingNewerIsUpToDateAndUnreachableWhenGitHubCannotBeReachedAsync(bool reached, string status)
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        var check = Check(data, new FakeInstaller { Finding = new InstallerFinding(reached, Option<string>.None) }, bus);

        var state = await check.CheckAsync(Cancellation);

        Assert.Equal(new UpdateState(Enum.Parse<UpdateStatus>(status), Option<AvailableUpdate>.None), state);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task DownloadingTheOfferedUpdateLeavesItReadyToRestartAndAnnouncesItAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        var installer = FakeInstaller.Offering("0.2.0");
        var check = Check(data, installer, bus);
        await check.CheckAsync(Cancellation);

        var state = await check.DownloadAsync(Cancellation);

        Assert.Equal((new UpdateState(UpdateStatus.Downloaded, Newer), 1, 0), (state, installer.Downloading, installer.Applied));
        Assert.Equal([new UpdateFound(Newer), new UpdateDownloaded(Newer)], bus.Published);
    }

    [Fact]
    public async Task AFailedDownloadKeepsTheUpdateSoItCanBeTriedAgainAsync()
    {
        await using var data = new TemporaryFolder();
        var installer = new FakeInstaller { Finding = new InstallerFinding(true, "0.2.0"), Downloads = false };
        var check = Check(data, installer, new RecordingBus());
        await check.CheckAsync(Cancellation);

        var failed = await check.DownloadAsync(Cancellation);
        var retried = await check.DownloadAsync(Cancellation);

        Assert.Equal(new UpdateState(UpdateStatus.DownloadFailed, Newer), failed);
        Assert.Equal((UpdateStatus.DownloadFailed, 2), (retried.Status, installer.Downloading));
    }

    [Fact]
    public async Task NothingIsDownloadedOrAppliedBeforeThePersonAsksAndOnlyADownloadedUpdateRestartsAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        var installer = FakeInstaller.Offering("0.2.0");
        var check = Check(data, installer, bus);

        await check.RunAsync(Cancellation);
        await check.Checking;
        var early = await check.RestartAsync(Cancellation);
        var untouched = (installer.Downloading, installer.Applied, early.Status);
        await check.DownloadAsync(Cancellation);
        await check.RestartAsync(Cancellation);

        Assert.Equal((0, 0, UpdateStatus.Available), untouched);
        Assert.Equal((1, 1), (installer.Downloading, installer.Applied));
        Assert.Equal(new RestartingToUpdate(Newer), bus.Published[^1]);
    }

    [Fact]
    public async Task AnUpdateDownloadedEarlierIsReadyToRestartWithoutAskingGitHubAsync()
    {
        await using var data = new TemporaryFolder();
        var installer = new FakeInstaller { Downloaded = "0.2.0" };
        var check = Check(data, installer, new RecordingBus());

        var state = await check.CheckAsync(Cancellation);

        Assert.Equal((new UpdateState(UpdateStatus.Downloaded, Newer), 0), (state, installer.Finds));
    }

    [Fact]
    public async Task AnUpdateFromTheReleasesPageIsNeverDownloadedInPlaceAsync()
    {
        await using var data = new TemporaryFolder();
        using var releases = FakeReleases.Listing(FakeReleases.Release("v0.2.0"));
        var check = new UpdateCheck(AvalaBuild.From("0.1.0"), new UpdatesFile(new AvalaPaths(data.Path)), new UpdateSources(new ReleaseFeed(new HttpClient(releases, disposeHandler: false)), Option<IUpdateInstaller>.None), new RecordingBus());
        var found = await check.CheckAsync(Cancellation);

        var asked = await check.DownloadAsync(Cancellation);

        Assert.Equal((false, found), (found.CanDownload, asked));
    }

    private static UpdateCheck Check(TemporaryFolder data, FakeInstaller installer, RecordingBus bus, FakeReleases? releases = null) =>
        new(
            AvalaBuild.From("0.1.0"),
            new UpdatesFile(new AvalaPaths(data.Path)),
            new UpdateSources(releases is null ? Option<ReleaseFeed>.None : new ReleaseFeed(new HttpClient(releases, disposeHandler: false)), Option<IUpdateInstaller>.Some(installer)),
            bus);
}
