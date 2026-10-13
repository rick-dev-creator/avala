using Avala.Sdk;
using Avala.Sdk.Updates;
using Avala.Testing;
using Avala.Workbench.Updates;

namespace Avala.Workbench.Tests.Updates;

public sealed class UpdateViewModelScripts
{
    private static readonly AvalaBuild Build = new("0.1.0", "5c371e6a1b2d3e4f5a6b");

    [Theory]
    [InlineData(nameof(UpdateStatus.NotChecked), "Not checked yet.")]
    [InlineData(nameof(UpdateStatus.Off), "Checking at startup is off on this machine.")]
    [InlineData(nameof(UpdateStatus.Checking), "Checking for updates…")]
    [InlineData(nameof(UpdateStatus.UpToDate), "Avala is up to date.")]
    [InlineData(nameof(UpdateStatus.Unreachable), "GitHub could not be reached to check for updates.")]
    public void ItSaysWhereTheCheckStands(string status, string said) =>
        ViewModelScript.Given(new UpdateViewModel(Build, new FakeUpdates { Latest = FakeUpdates.Of(Enum.Parse<UpdateStatus>(status)) }, new FakeLinks()))
            .Then(update => Assert.Equal((said, false, false), (update.Status, update.IsAvailable, update.UpdateCommand.CanExecute(null))));

    [Fact]
    public async Task CheckingNowAsksAndOffersTheNewerVersion()
    {
        var updates = new FakeUpdates();
        var update = new UpdateViewModel(Build, updates, new FakeLinks());

        await update.CheckCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.Checks);
        Assert.Equal(("Version 0.2.0 is available.", true, true), (update.Status, update.IsAvailable, update.UpdateCommand.CanExecute(null)));
    }

    [Fact]
    public async Task DownloadOpensTheReleasePageOfAnUpdateThatDoesNotInstallInPlaceAndAnUnopenedPageGivesItsAddress()
    {
        var links = new FakeLinks();
        var update = new UpdateViewModel(Build, new FakeUpdates { Latest = FakeUpdates.Found }, links);
        var refused = new UpdateViewModel(Build, new FakeUpdates { Latest = FakeUpdates.Found }, new FakeLinks { Refusal = FileOpenError.Unavailable });

        await update.UpdateCommand.ExecuteAsync(null);
        await refused.UpdateCommand.ExecuteAsync(null);

        Assert.Equal([FakeUpdates.Newer.Release], links.Opened);
        Assert.Equal("Version 0.2.0 is available.", update.Status);
        Assert.Equal($"The browser could not be opened. The release is at {FakeUpdates.Newer.Release}.", refused.Status);
    }

    [Fact]
    public void RefreshingShowsWhatTheStartupCheckFoundSince()
    {
        var updates = new FakeUpdates();
        var update = new UpdateViewModel(Build, updates, new FakeLinks());

        updates.Latest = FakeUpdates.Found;
        update.Refresh();

        Assert.Equal(("Version 0.2.0 is available.", true), (update.Status, update.IsAvailable));
    }

    [Fact]
    public async Task DownloadFetchesAnUpdateThatInstallsInPlaceThenRestartToUpdateAppliesIt()
    {
        var links = new FakeLinks();
        var updates = new FakeUpdates { Latest = FakeUpdates.Installable };
        var update = new UpdateViewModel(Build, updates, links);
        var offered = (update.Action, update.UpdateCommand.CanExecute(null));

        await update.UpdateCommand.ExecuteAsync(null);
        var downloaded = (update.Status, update.Action);
        await update.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(("Download", true), offered);
        Assert.Equal(("Version 0.2.0 is downloaded. Restart Avala to update.", "Restart to update"), downloaded);
        Assert.Equal((1, 1), (updates.Downloading, updates.Restarts));
        Assert.Empty(links.Opened);
    }
}
