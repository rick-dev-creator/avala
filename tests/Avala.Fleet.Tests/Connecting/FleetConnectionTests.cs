using Avala.Fleet.Connecting;
using Avala.Fleet.Connection;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.Reading;
using Avala.Fleet.Recording;
using Avala.Sdk;

namespace Avala.Fleet.Tests.Connecting;

public sealed class FleetSessionTests
{
    private readonly FakeKeychain keychain = new();

    [Fact]
    public async Task TheKeyIsReadOnceAtStartup()
    {
        var session = await Fleets.ConnectedAsync(keychain);

        _ = session.Credentials();
        _ = session.Credentials();

        Assert.Equal((1, ConnectionState.Connected), (keychain.Reads, session.State));
    }

    [Fact]
    public async Task AFailedReadWaitsForRetry()
    {
        keychain.FailsReading = true;
        var session = await Fleets.ConnectedAsync(keychain);
        var readsBefore = keychain.Reads;
        _ = session.Credentials();
        keychain.FailsReading = false;

        var retried = await session.RetriedAsync(TestContext.Current.CancellationToken);

        Assert.Equal((1, true, ConnectionState.Connected), (readsBefore, retried, session.State));
    }

    [Fact]
    public async Task ARefusedKeySurvivesARestart()
    {
        var file = new FakeFleetFile(Fleets.Settings());
        keychain.Keys[Fleets.Server] = Fleets.Key;
        var first = new FleetSession(file, keychain);
        await first.RunAsync(TestContext.Current.CancellationToken);
        await first.RefusedAsync(Fleets.Settings().Scope, TestContext.Current.CancellationToken);

        var restarted = new FleetSession(file, keychain);
        await restarted.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((ConnectionState.KeyRefused, true), (restarted.State, restarted.Credentials().IsNone));
    }

    [Fact]
    public async Task ARefusalFromAnotherScopeIsIgnored()
    {
        keychain.Keys[Fleets.Server] = Fleets.Key;
        var session = new FleetSession(new FakeFleetFile(Fleets.Settings()), keychain);
        await session.RunAsync(TestContext.Current.CancellationToken);

        var refused = await session.RefusedAsync(new Connection.FleetScope("https://old.example", Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal((false, ConnectionState.Connected), (refused, session.State));
    }
}

public sealed class FleetConnectionTests : IAsyncDisposable
{
    private readonly FakeKeychain keychain = new();

    private readonly FakeFactStore store = new();

    private readonly FakeFleetServer server = new();

    private readonly FakeFleetFile file = new();

    private readonly Outbox outbox;

    public FleetConnectionTests() => outbox = new Outbox(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<Outbox>.Instance);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConnectingStoresTheKeyInTheKeychainAndNotInTheFile()
    {
        var connection = await ConnectionAsync();

        var outcome = await connection.ConnectAsync("https://fleet.example/", $" {Fleets.Key} ", Cancellation);

        Assert.Equal(
            (ConnectOutcome.Connected, Fleets.Key, "desktop"),
            (outcome, keychain.Keys[Fleets.Server], file.Settings.Match(found => found.InstanceName, () => string.Empty)));
        Assert.DoesNotContain(Fleets.Key, file.Settings.Match(found => found.ToString(), () => string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectingRefusesAKeyNotShapedLikeOne() =>
        Assert.Equal(ConnectOutcome.NotAKey, await (await ConnectionAsync()).ConnectAsync(Fleets.Server, "avk_short", Cancellation));

    [Fact]
    public async Task ConnectingIsRefusedWithoutAKeychain()
    {
        keychain.Available = false;

        Assert.Equal((ConnectOutcome.NoKeychain, 0), (await (await ConnectionAsync()).ConnectAsync(Fleets.Server, Fleets.Key, Cancellation), keychain.Keys.Count));
    }

    [Fact]
    public async Task ConnectingRefusesAnInsecureAddress() =>
        Assert.Equal(ConnectOutcome.InsecureAddress, await (await ConnectionAsync()).ConnectAsync("http://fleet.example", Fleets.Key, Cancellation));

    [Fact]
    public async Task AKeyTheServerRefusesIsNotKept()
    {
        server.Identifies = ServerAnswer.KeyRefused;

        Assert.Equal((ConnectOutcome.KeyRefused, 0), (await (await ConnectionAsync()).ConnectAsync(Fleets.Server, Fleets.Key, Cancellation), keychain.Keys.Count));
    }

    [Fact]
    public async Task ConnectingToANewInstanceDeletesTheOldScope()
    {
        store.Entries.Add(new StoredEntry("https://old.example|0199f6a0-0000-7000-8000-000000000999", new Facts.Fact(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new Facts.JobProgressedFact(Guid.NewGuid(), "running"))));
        keychain.Keys["https://old.example"] = Fleets.Key;
        await file.WriteAsync(Fleets.Settings() with { Server = "https://old.example" }, Cancellation);
        var connection = await ConnectionAsync();

        await connection.ConnectAsync(Fleets.Server, Fleets.Key, Cancellation);

        Assert.Equal((0, false), (store.Entries.Count, keychain.Keys.ContainsKey("https://old.example")));
    }

    [Fact]
    public async Task TestingTheConnectionNamesTheInstance()
    {
        keychain.Keys[Fleets.Server] = Fleets.Key;
        await file.WriteAsync(Fleets.Settings(), Cancellation);
        var connection = await ConnectionAsync();

        Assert.Equal((ConnectOutcome.Connected, "desktop"), (await connection.TestAsync(Cancellation), (await connection.StatusAsync(Cancellation)).InstanceName));
    }

    [Fact]
    public async Task DisconnectingRemovesTheKeyTheFileAndTheOutbox()
    {
        var connection = await ConnectionAsync();
        await connection.ConnectAsync(Fleets.Server, Fleets.Key, Cancellation);
        store.Entries.Add(new StoredEntry(Fleets.Scope, new Facts.Fact(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new Facts.JobProgressedFact(Guid.NewGuid(), "running"))));

        var outcome = await connection.DisconnectAsync(Cancellation);

        Assert.Equal(
            (ConnectOutcome.Disconnected, FleetState.NotConnected, 0, 0, true),
            (outcome, (await connection.StatusAsync(Cancellation)).State, keychain.Keys.Count, store.Entries.Count, file.Settings.IsNone));
    }

    [Fact]
    public async Task ADisconnectTheKeychainRefusesKeepsTheConnection()
    {
        var connection = await ConnectionAsync();
        await connection.ConnectAsync(Fleets.Server, Fleets.Key, Cancellation);
        keychain.FailsRemoving = true;

        var outcome = await connection.DisconnectAsync(Cancellation);

        Assert.Equal(
            (ConnectOutcome.KeyNotRemoved, FleetState.Connected, true),
            (outcome, (await connection.StatusAsync(Cancellation)).State, file.Settings.IsSome));
    }

    [Fact]
    public async Task WithoutAKeychainADisconnectForgetsHereAndSaysTheKeyMayRemain()
    {
        var connection = await ConnectionAsync();
        await connection.ConnectAsync(Fleets.Server, Fleets.Key, Cancellation);
        keychain.FailsReading = true;
        keychain.FailsRemoving = true;
        var restarted = new FleetSession(file, keychain);
        await restarted.RunAsync(Cancellation);

        var outcome = await new FleetConnection(restarted, outbox, server, keychain).DisconnectAsync(Cancellation);

        Assert.Equal((ConnectOutcome.DisconnectedKeyKept, true), (outcome, file.Settings.IsNone));
    }

    [Fact]
    public async Task PausingStopsTheSessionFromRecording()
    {
        var connection = await ConnectionAsync();
        await connection.ConnectAsync(Fleets.Server, Fleets.Key, Cancellation);

        var status = await connection.PauseAsync(paused: true, Cancellation);

        Assert.Equal((FleetState.Paused, false), (status.State, file.Settings.Match(found => found.Enabled, () => true)));
    }

    [Fact]
    public async Task TheStatusFollowsADelivery()
    {
        var connection = await ConnectionAsync();
        await connection.ConnectAsync(Fleets.Server, Fleets.Key, Cancellation);
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await using var statuses = connection.WatchAsync(watching.Token).GetAsyncEnumerator(watching.Token);
        await statuses.MoveNextAsync();

        await outbox.AppendAsync(Fleets.Scope, new Facts.Fact(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new Facts.JobProgressedFact(Guid.NewGuid(), "running")), Cancellation);
        await statuses.MoveNextAsync();

        Assert.Equal(1, statuses.Current.Waiting);
    }

    public ValueTask DisposeAsync() => outbox.DisposeAsync();

    private async Task<FleetConnection> ConnectionAsync()
    {
        var session = new FleetSession(file, keychain);
        await session.RunAsync(Cancellation);

        return new FleetConnection(session, outbox, server, keychain);
    }
}

public sealed class FleetActivityReaderTests
{
    private readonly FakeKeychain keychain = new();

    private readonly FakeFleetServer server = new();

    [Fact]
    public async Task NotConnectedIsSaidWithoutAsking()
    {
        var reader = new FleetActivityReader(await Fleets.SessionAsync(keychain, Option<FleetSettings>.None), server);

        Assert.Equal((FleetReadState.NotConnected, 0), ((await reader.RepositoriesAsync(TestContext.Current.CancellationToken)).State, server.Asked.Count));
    }

    [Fact]
    public async Task RepositoriesAreReadWithTheKey()
    {
        var reader = new FleetActivityReader(await Fleets.ConnectedAsync(keychain), server);

        await reader.RepositoriesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([$"repositories {Fleets.Server} {Fleets.Key}"], server.Asked);
    }

    [Fact]
    public async Task ARefusedKeyIsSaid()
    {
        keychain.Keys[Fleets.Server] = Fleets.Key;
        var reader = new FleetActivityReader(await Fleets.SessionAsync(keychain, Fleets.Settings(refused: true)), server);

        Assert.Equal(FleetReadState.KeyRefused, (await reader.RepositoriesAsync(TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task APausedFleetIsSaidWithoutAsking()
    {
        keychain.Keys[Fleets.Server] = Fleets.Key;
        var reader = new FleetActivityReader(await Fleets.SessionAsync(keychain, Fleets.Settings(enabled: false)), server);

        Assert.Equal((FleetReadState.Paused, 0), ((await reader.RepositoriesAsync(TestContext.Current.CancellationToken)).State, server.Asked.Count));
    }
}
