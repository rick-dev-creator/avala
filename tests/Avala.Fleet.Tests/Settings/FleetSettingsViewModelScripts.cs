using Avala.Fleet.Connecting;
using Avala.Fleet.Recording;
using Avala.Fleet.Settings;
using Avala.Testing;

namespace Avala.Fleet.Tests.Settings;

public sealed class FleetSettingsViewModelScripts : IAsyncDisposable
{
    private readonly FakeKeychain keychain = new();

    private readonly FakeFactStore store = new();

    private readonly FakeFleetServer server = new();

    private readonly FakeFleetFile file = new();

    private readonly TestUiDispatcher ui = new();

    private readonly Outbox outbox;

    public FleetSettingsViewModelScripts() => outbox = new Outbox(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<Outbox>.Instance);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConnectingShowsTheInstanceAndEmptiesTheKeyBox()
    {
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;

        await section.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(
            (true, "desktop", string.Empty, false),
            (await ui.ReadAsync(() => section.IsConnected), await ui.ReadAsync(() => section.Instance.Value), section.KeyBox, await ui.ReadAsync(() => section.HasProblem)));
    }

    [Fact]
    public async Task TheKeyBoxIsEmptyAfterConnectingEvenWhenRefused()
    {
        server.Identifies = Avala.Fleet.Delivering.ServerAnswer.KeyRefused;
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;

        await section.ConnectCommand.ExecuteAsync(null);

        Assert.Equal((string.Empty, "The server refused the key"), (section.KeyBox, await ui.ReadAsync(() => section.Problem)));
    }

    [Fact]
    public async Task AnInsecureAddressIsExplained()
    {
        var section = await SectionAsync();
        section.Server = "http://fleet.example";
        section.KeyBox = Fleets.Key;

        await section.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("The key is only sent over https, or over http to this computer", await ui.ReadAsync(() => section.Problem));
    }

    [Fact]
    public async Task TheStatusFollowsADelivery()
    {
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;
        await section.ConnectCommand.ExecuteAsync(null);
        section.Activate();

        await outbox.AppendAsync(Fleets.Scope, new Facts.Fact(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new Facts.JobProgressedFact(Guid.NewGuid(), "running")), Cancellation);

        await ui.UntilAsync(() => section.Waiting == "1 event waiting", () => section.Waiting);

        Assert.Equal("1 event waiting", await ui.ReadAsync(() => section.Waiting));
        section.Deactivate();
    }

    [Fact]
    public async Task AServerThatStoppedAnsweringIsShownUntilItAnswers()
    {
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;
        await section.ConnectCommand.ExecuteAsync(null);

        outbox.Unanswered(DateTimeOffset.UnixEpoch);
        await section.TestCommand.ExecuteAsync(null);
        var shown = await ui.ReadAsync(() => (section.HasUnanswered, section.Unanswered.StartsWith("The server has not answered since", StringComparison.Ordinal)));
        outbox.Answered();
        await section.TestCommand.ExecuteAsync(null);

        Assert.Equal(((true, true), false), (shown, await ui.ReadAsync(() => section.HasUnanswered)));
    }

    [Fact]
    public async Task ADisconnectTheKeychainRefusesIsExplained()
    {
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;
        await section.ConnectCommand.ExecuteAsync(null);
        keychain.FailsRemoving = true;

        await section.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal(
            (true, "The keychain did not remove the key, so this instance stays connected"),
            (await ui.ReadAsync(() => section.IsConnected), await ui.ReadAsync(() => section.Problem)));
    }

    [Fact]
    public async Task PausingAndDisconnectingAreShown()
    {
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;
        await section.ConnectCommand.ExecuteAsync(null);

        await section.TogglePauseCommand.ExecuteAsync(null);
        var paused = await ui.ReadAsync(() => section.IsPaused);
        await section.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal((true, true, 0), (paused, await ui.ReadAsync(() => section.IsDisconnected), keychain.Keys.Count));
    }

    [Fact]
    public async Task RejectedEventsAreListed()
    {
        var section = await SectionAsync();
        section.Server = Fleets.Server;
        section.KeyBox = Fleets.Key;
        await section.ConnectCommand.ExecuteAsync(null);
        store.Rejections.Add((Fleets.Scope, new RejectionNote("usageReported", "a cost has at most 6 decimals", DateTimeOffset.UnixEpoch)));

        await section.TestCommand.ExecuteAsync(null);

        Assert.Equal("a cost has at most 6 decimals", await ui.ReadAsync(() => Assert.Single(section.Rejections).Reason));
    }

    public async ValueTask DisposeAsync()
    {
        ui.Dispose();
        await outbox.DisposeAsync();
    }

    private async Task<FleetSettingsViewModel> SectionAsync()
    {
        var session = new FleetSession(file, keychain);
        await session.RunAsync(Cancellation);

        return new FleetSettingsViewModel(new FleetConnection(session, outbox, server, keychain), ui);
    }
}

public sealed class FleetRejectionRowViewModelScripts
{
    [Fact]
    public void ARowShowsTheKindAndTheReason()
    {
        var row = new FleetRejectionRowViewModel(new RejectionNote("usageReported", "a cost has at most 6 decimals", DateTimeOffset.UnixEpoch));

        Assert.Equal(("usageReported", "a cost has at most 6 decimals"), (row.Kind, row.Reason));
    }
}
