using Avala.Sdk;
using Avala.Sdk.Updates;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workbench.Tests.Updates;

public sealed class UpdateNoticeViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();
    private readonly Pulse pulse = new(new JobBoard());

    [Fact]
    public async Task TheNoticeStaysHiddenUntilAnUpdateIsFoundThenNamesItsVersionAsync()
    {
        var updates = new FakeUpdates { Latest = FakeUpdates.Of(UpdateStatus.UpToDate) };
        using var notice = Notice(updates, new FakeLinks());
        notice.Activate();
        await ui.PresentedAsync(notice, () => notice.Revision > 0, () => notice.Text);
        var before = await ui.ReadAsync(() => (notice.IsShown, notice.Text));

        updates.Latest = FakeUpdates.Found;
        await pulse.HandleAsync(new UpdateFound(FakeUpdates.Newer), TestContext.Current.CancellationToken);

        await ui.PresentedAsync(notice, () => notice.IsShown, () => notice.Text);
        Assert.Equal((false, string.Empty), before);
        Assert.Equal(("Version 0.2.0 is available", true), await ui.ReadAsync(() => (notice.Text, notice.OpenCommand.CanExecute(null))));
    }

    [Fact]
    public async Task OpeningTheNoticeOpensTheReleasePageAndAnUnopenedPageGivesItsAddressAsync()
    {
        var links = new FakeLinks();
        using var notice = Notice(new FakeUpdates { Latest = FakeUpdates.Found }, links);
        using var refused = Notice(new FakeUpdates { Latest = FakeUpdates.Found }, new FakeLinks { Refusal = FileOpenError.Refused });
        notice.Activate();
        refused.Activate();
        await ui.PresentedAsync(notice, () => notice.IsShown, () => notice.Text);
        await ui.PresentedAsync(refused, () => refused.IsShown, () => refused.Text);

        await ui.RunAsync(() => notice.OpenCommand.ExecuteAsync(null));
        await ui.RunAsync(() => refused.OpenCommand.ExecuteAsync(null));

        Assert.Equal([FakeUpdates.Newer.Release], links.Opened);
        Assert.Equal((string.Empty, $"The browser could not be opened. The release is at {FakeUpdates.Newer.Release}."), await ui.ReadAsync(() => (notice.Note, refused.Note)));
    }

    [Fact]
    public async Task ClickingAnUpdateThatInstallsInPlaceDownloadsItThenOffersToRestartAndRestartsAsync()
    {
        var links = new FakeLinks();
        var updates = new FakeUpdates { Latest = FakeUpdates.Installable };
        using var notice = Notice(updates, links);
        notice.Activate();
        await ui.PresentedAsync(notice, () => notice.IsShown, () => notice.Text);

        await ui.RunAsync(() => notice.OpenCommand.ExecuteAsync(null));
        var downloaded = await ui.ReadAsync(() => (notice.Text, notice.Hint, notice.Note));
        await ui.RunAsync(() => notice.OpenCommand.ExecuteAsync(null));

        Assert.Equal(("Restart to update", "Restart Avala and update", string.Empty), downloaded);
        Assert.Equal((1, 1), (updates.Downloading, updates.Restarts));
        Assert.Empty(links.Opened);
    }

    [Fact]
    public async Task AFailedDownloadKeepsTheNoticeAndSaysToTryAgainAsync()
    {
        using var notice = Notice(new FakeUpdates { Latest = FakeUpdates.Installable, Downloads = false }, new FakeLinks());
        notice.Activate();
        await ui.PresentedAsync(notice, () => notice.IsShown, () => notice.Text);

        await ui.RunAsync(() => notice.OpenCommand.ExecuteAsync(null));

        Assert.Equal(("Version 0.2.0 is available", "The download failed. Click to try again.", true), await ui.ReadAsync(() => (notice.Text, notice.Note, notice.OpenCommand.CanExecute(null))));
    }

    public void Dispose() => ui.Dispose();

    private UpdateNoticeViewModel Notice(FakeUpdates updates, FakeLinks links) => new(updates, new LiveFeed(pulse, ui, NullLogger<LiveFeed>.Instance), links);
}
